# NimbusLedger deployment and pilot validation

The supported initial topology is a domain-connected Windows worker plus an IIS
or Kestrel dashboard. Version 2.0.3 targets .NET 10. Azure hosting is not configured
by this repository; LDAP connectivity and Windows authentication must not be
assumed to work on an arbitrary public web app.

## 1. Prepare identities, connectivity and storage

- Install the .NET 10 runtime for the worker and ASP.NET Core Hosting Bundle for IIS.
- Permit outbound LDAPS and HTTPS to the configured directory, Graph and optional
  AdminService. Trust the real issuing CA; certificate bypass is rejected.
- Start with LDAP read permissions and Graph application read permissions only.
  Run under a dedicated service identity or gMSA as appropriate. Do not grant delete
  permissions during the read-only pilot.
- Configure a real app-only Graph identity. Placeholders in sample settings are
  not usable credentials. Managed identity requires a host that actually provides it.
- Use a shared absolute snapshot path, for example `C:\Apps\NimbusLedger\data`.
  Grant worker Modify and dashboard Read. Restrict access to authorized operators.
- Keep the execution lease, snapshots and audit on storage whose locking and
  atomic-replacement behavior has been validated. Do not run independent workers
  against independent copies of the same configuration and assume a global budget.

## 2. Build and publish

From the repository root:

```powershell
dotnet test NimbusLedger.Tests\NimbusLedger.Tests.csproj -c Release
dotnet publish NimbusLedger.Worker -c Release -o publish\worker
dotnet publish NimbusLedger.Web -c Release -o publish\web
```

Inspect deployment configuration locally before transferring artifacts. Build
output includes appsettings files; never distribute a developer's secrets or
customer-specific settings as a generic package.

## 3. Configure the worker

Use environment variables or an approved secret provider. Standard hierarchical
keys use double underscores:

```powershell
$env:HybridLedger__ActiveDirectory__LdapServer = 'dc.example.com'
$env:HybridLedger__ActiveDirectory__BaseDn = 'DC=example,DC=com'
$env:HybridLedger__ActiveDirectory__UseSsl = 'true'
$env:HybridLedger__Snapshot__RootPath = 'C:\Apps\NimbusLedger\data'
$env:HybridLedger__Cleanup__Enabled = 'false'
$env:HybridLedger__Cleanup__DryRun = 'true'
$env:HybridLedger__Sccm__Enabled = 'false'
```

These values are placeholders. Set the persistent environment for the actual
service identity; variables in an interactive shell do not automatically become
service configuration. Restart the service after configuration changes.

For ConfigMgr, set `HybridLedger__Sccm__Enabled=true` and an HTTPS URL ending in
`/AdminService`, including any required CMG prefix. The client uses the service's
Windows credentials unless explicit credentials are supplied securely. Redirects
are disabled; pagination cannot leave the configured origin or service path.
Both `v1.0/Device` and `wmi/SMS_R_System` must be readable. Validate their properties
and AD GUID encoding against the actual site version.

## 4. Install the Windows Service

The worker registers Windows Service lifetime integration. Publish to its final
directory, then use an elevated shell:

```powershell
New-Service -Name 'NimbusLedger.Worker' `
    -BinaryPathName '"C:\Apps\NimbusLedger\Worker\NimbusLedger.Worker.exe"' `
    -StartupType Automatic
```

Configure the service identity and recovery through the organization's approved
administration process. Do not paste service passwords into scripts, Git or logs.
Start only after its environment and storage ACLs are configured.

## 5. Protect the dashboard

- Configure `WebAccess__AllowedGroups__0` with a real authorized Windows group.
  Additional array indices allow additional groups. There is no default access.
- Set `HybridLedger__Snapshot__RootPath` to the same absolute directory as the worker.
- For IIS, enable Windows Authentication, disable anonymous application access and
  use HTTPS. Server-level authentication can also protect the health routes, so
  configure authenticated probes or a restricted health-check path as appropriate.
- Kestrel uses Negotiate authentication. Validate Kerberos/SPNs and connection
  affinity before placing a proxy in front of it; never replace authentication
  with an untrusted identity header.
- The application enforces the Windows-group policy on dashboard and snapshot API.
  TLS alone is not authorization.

## 6. Read-only acceptance checks

1. Confirm exactly one worker can acquire the snapshot-directory execution lease.
2. Compare all inventory counts and a representative sample of stable identities
   against AD, Graph and ConfigMgr. Include renamed and re-enrolled devices.
3. Exercise duplicates, FQDN/name differences, missing GUIDs and unknown activity.
   Verify that ambiguous/name-only matches are visible and cannot authorize deletion.
4. Interrupt a source and confirm no partial snapshot replaces the previous one.
   Let the previous snapshot age and confirm `/health/ready` returns unhealthy.
5. Verify unauthenticated and out-of-group requests cannot read inventory data.
   Anonymous health responses must not disclose device records.
6. Read the dashboard while publishing repeated snapshots. Confirm valid JSON,
   no partial reads, correct retention and adequate disk capacity.
7. Verify shutdown, restart, service recovery and log forwarding on the real host.

## 7. Preview and execution gates

Keep both dry-run flags true while testing cleanup. Configure explicit device GUID
and ConfigMgr resource-ID allowlists, exclusions, inventory minimums and budgets.
Defaults do not authorize cleanup of all old objects.

Cloud planning requires `Cleanup:Enabled=true` plus the selected `DeleteIntune`
and/or `DeleteEntra` flags. ConfigMgr planning additionally requires
`Sccm:CleanupEnabled=true`. A deletion requires `Cleanup:DryRun=false`; ConfigMgr
also requires `Sccm:DryRun=false`. Grant destructive permissions only after a
reviewed preview matches the intended pilot scope.

Each module has its own per-run count budget. Percentage limits are based on
scoped inventory. `ObsoleteDaysThreshold` applies to last activity age; the source
does not prove when the obsolete flag first changed.

Execution recollects inventories, verifies the plan, writes a durable audit-start
record and then calls the backend. Any preflight difference aborts the module.
A failure after one successful operation cannot roll that operation back:
investigate audit outcomes and backend state before proceeding.

Missing AD objects are deliberately not auto-deleted from other systems. Supporting
that case safely requires a separate approved lifecycle/tombstone process.

## 8. Updates and recovery

Stop the worker before replacing binaries. Back up configuration, snapshots and
audit history with appropriate access controls. Preserve the service environment,
restart, and repeat the read-only checks before re-enabling cleanup.

Snapshot history retention does not rotate `cleanup-audit.jsonl`. Ship and retain
the journal according to operational policy, monitoring disk usage. Its records
are not cryptographically immutable.

Do not describe this release as universally production-certified until the actual
directory topology, permissions, API schemas, authentication and destructive pilot
have been independently accepted.
