# NimbusLedger

Version **2.0.3** reconciles Windows device inventories across Active Directory,
Microsoft Entra ID, Intune and optional Configuration Manager. A .NET 10 worker
collects inventories, persists JSON snapshots and proposes narrowly scoped cleanup
operations. A Windows-authenticated Razor Pages dashboard reads those snapshots.

This release introduces breaking configuration and interface changes. It is a
hardening baseline for laboratory and read-only pilot validation, not a claim that
every production topology has been certified.

## Components

| Project | Responsibility |
| --- | --- |
| `NimbusLedger.Core` | Inventory models, shared identity matching and contracts. |
| `NimbusLedger.Infrastructure` | Paged LDAP/Graph inventories, reconciliation, cloud cleanup planning and storage. |
| `NimbusLedger.Sccm` | Paged AdminService inventories, discovered AD GUID correlation and guarded cleanup. |
| `NimbusLedger.Worker` | Windows Service hosting, scheduling and single-writer execution lease. |
| `NimbusLedger.Web` | Authorized dashboard, snapshot API and health endpoints. |
| `NimbusLedger.Tests` | Deterministic regression tests; no live tenant or directory access. |

## Identity and inventory boundaries

- Stable GUID matches take precedence. Computer account names, FQDNs and trailing
  `$` are normalized for reporting, but **a name-only match never authorizes deletion**.
- Duplicate matches are explicitly ambiguous instead of being treated as proof of presence.
- Entra object IDs, Entra device IDs and Intune managed-device IDs remain separate.
- ConfigMgr combines `v1.0/Device` activity data with AD `ObjectGUID` discovery
  from `wmi/SMS_R_System`. Missing discovery GUIDs allow name-only reporting but
  cannot authorize ConfigMgr cleanup.
- A source failure aborts reconciliation. The previous snapshot remains available
  with its original timestamp; it is not relabelled as current.
- ConfigMgr is optional. A disabled source is represented as not collected, not
  as a successfully collected empty inventory.

## Build and test

Install a stable .NET 10 SDK, then run from the repository root:

```powershell
dotnet build NimbusLedger.sln -c Release
dotnet test NimbusLedger.Tests\NimbusLedger.Tests.csproj -c Release
```

CI runs the same regressions on Windows and Linux and checks transitive dependency
advisories. This does not replace live LDAP, Graph, Kerberos and AdminService tests.

## Configuration

Both executable projects read standard .NET configuration providers. Keep secrets
out of tracked settings; use environment variables or an approved secret provider.
The worker's `HybridLedger` options include:

| Section | Important settings |
| --- | --- |
| `ActiveDirectory` | `LdapServer`, `BaseDn`, `Port=636`, `UseSsl=true`, `ActivityWindowDays>=14`. Certificate validation is mandatory. |
| `Graph` | Tenant and client identity, `.default` scope; managed identity or app-only credentials with least privilege. |
| `Snapshot` | Shared absolute `RootPath`, `LatestFileName`, positive `HistorySize` and `MaxAge`. |
| `Scheduler` | `StartupDelay` and collection `Interval`. |
| `Cleanup` | Disabled by default; `DryRun=true`. Explicit `AllowedAzureAdDeviceIds`, exclusions, inventory minimums, age limits and deletion budgets. |
| `Sccm` | `Enabled=false`; full HTTPS `AdminServiceBaseUrl`. Separate cleanup enablement, dry-run flag and explicit resource-ID allowlist. |

For the web application, configure at least one authorized Windows group:

```powershell
$env:WebAccess__AllowedGroups__0 = 'EXAMPLE\LedgerReaders'
$env:HybridLedger__Snapshot__RootPath = 'C:\Apps\NimbusLedger\data'
dotnet run --project NimbusLedger.Web
```

These are illustrative placeholders, not infrastructure defaults. An empty group
list prevents startup. There is no anonymous-development bypass.

## Cleanup safety contract

Start with read-only permissions and both cleanup modules disabled. A preview
requires explicit scope; execution additionally requires all applicable dry-run
flags to be disabled intentionally.

Cloud cleanup requires an allowlisted Windows hybrid-joined Entra device, a unique
stable AD identity, known old AD activity and change timestamps, and exactly one
linked Intune record with known old activity. Recent Entra sign-in activity also
protects the device. Missing AD objects, cloud-only devices, unknown timestamps
and name-only matches are not deletion authority. Zero LDAP FILETIME and pre-epoch
sentinel timestamps are unknown activity, not evidence that a device is old.

ConfigMgr cleanup additionally requires an allowlisted resource ID and its
discovered AD object GUID. The inactive/obsolete flag is combined with
`LastActiveTime`; `ObsoleteDaysThreshold` means minimum activity age, not proven
time spent in the obsolete state.

Both modules reject incomplete, stale, oversized or ambiguous plans. Budgets are
per module and per run; cloud operation count includes each Intune and Entra delete.
The percentage limit uses the explicit scoped inventory, not the entire tenant.
No oversized plan is silently truncated. Defaults are five operations and five
percent, so a small pilot scope normally needs an explicit percentage adjustment.

Before execution, inventories are recollected and the proposed targets must remain
unchanged. Intune deletion precedes Entra deletion. `cleanup-audit.jsonl` is flushed
before and after each destructive call; failure to write the initial audit record
prevents the call. A `Started` record without `Completed` represents an uncertain
outcome requiring investigation. Transport failures and interruptions additionally
record `OutcomeUnknown` when audit storage remains available; that is not proof
that the backend did not apply the operation. This local journal is **not immutable or
tamper-proof**, and an external change can still occur after preflight.

## Storage and health

Worker and dashboard must use the same snapshot directory. Only one worker may
hold its `.worker.lock` lease at a time. Snapshots are published by same-filesystem
replacement, not in-place truncation. Readers allow delete sharing and bounded
retries for Windows replacement locks.

- `/` and `/api/snapshot`: Windows authentication plus an allowed group.
- `/health/live`: anonymous process liveness, no inventory data.
- `/health/ready`: anonymous status only; healthy requires a complete current snapshot.

The web identity needs read access only. The worker needs modify access to snapshot,
history, lease and audit files. Use a trusted local filesystem or explicitly
validate atomic replacement and locking semantics on the chosen SMB storage.

See `docs/DEPLOYMENT.md` for installation and pilot acceptance checks. No Azure
resources are provisioned automatically. The repository does not currently declare
a project-wide license; third-party frontend notices are not a license for this code.
