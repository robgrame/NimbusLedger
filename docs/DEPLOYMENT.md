# On-Premises Deployment Guide (No Azure Services)

Scope: Deploy `AdIntuneLedger.Worker` as a Windows Service and `AdIntuneLedger.Web` behind IIS on Windows Server, using local/server resources only. This guide assumes domain-joined servers and no use of Azure PaaS services.

- Worker: background reconciliation generating snapshots and logs.
- Web: Razor Pages dashboard reading the latest snapshot.
- Optional: SCCM cleanup module using the Configuration Manager Admin Service.
- Optional: Cloud cleanup (Entra/Intune) with Intune-freshness protection and dry-run mode.

1) Prepare servers
- OS: Windows Server 2019/2022 (domain-joined).
- Networking:
  - Worker ? Domain Controllers (LDAP/LDAPS): TCP 389/636 as required.
  - Worker ? Microsoft Graph (if used): TCP 443 outbound.
  - Worker ? SCCM Admin Service (if used): TCP 443 outbound to your site server AdminService URL.
  - Web ? Clients: TCP 443 inbound.
  - Web ? Worker shared storage: if using UNC for snapshots.
- Software:
  - Worker and Web: Install the .NET runtime matching the project target (e.g., .NET runtime supporting the target framework).
  - Web only: Install ASP.NET Core Hosting Bundle for IIS integration.
  - IIS role: Web-Server, Web-WebServer, Web-Http-Redirect, Web-Request-Monitor, Web-Log-Libraries, and required ASP.NET Core Module (installed by hosting bundle).
- Certificates:
  - Web: Server certificate for HTTPS binding in IIS.
  - LDAPS: Ensure the server trusts the DC’s LDAPS certificate chain.
  - SCCM Admin Service: Ensure the worker trusts the site server TLS chain.

2) Service accounts and identities
- Active Directory access:
  - Create a domain service account (least privilege) with rights to query LDAP and read `lastLogonTimestamp`.
  - Grant “Log on as a service” right on the worker server.
- Microsoft Graph (if used):
  - Use an app registration with application permissions for read and optionally delete operations you enable.
  - Grant admin consent.
- SCCM Admin Service (optional):
  - Run the worker under an account authorized to call the Admin Service (RBAC permissions to read devices and delete resources).

3) Build and publish artifacts
- On a build machine/dev box, from the repository root:
  - Publish worker: `dotnet publish AdIntuneLedger.Worker -c Release -o publish\worker`
  - Publish web: `dotnet publish AdIntuneLedger.Web -c Release -o publish\web`
- Copy the contents of `publish\worker` and `publish\web` to the target servers.

4) Layout on servers
- Create application folders (example):
  - `C:\Apps\AdIntuneLedger\Worker`
  - `C:\Apps\AdIntuneLedger\Web`
  - `C:\Apps\AdIntuneLedger\data`
  - `C:\Apps\AdIntuneLedger\logs`
- Permissions:
  - Grant Modify to the worker service account on `data` and `logs`.
  - Grant Read (or Modify if needed) to the IIS App Pool identity on `data` and Read on `Web` folder.
  - If using a UNC path for `data`, grant share and NTFS permissions to both identities.

5) Configure application settings
- Each app reads the `HybridLedger` section. Recommended: use environment variables for secrets and machine-specific paths.
- Critical settings:
  - `HybridLedger:ActiveDirectory` ? `LdapServer`, `Port`, `UseSsl`, `BaseDn`, `ActivityWindowDays`, `Username`, `Password`.
  - `HybridLedger:Graph` ? `TenantId`, `Scopes`.
  - `HybridLedger:Snapshot` ? `RootPath` (use an absolute path, e.g., `C:\Apps\AdIntuneLedger\data`) and `LatestFileName`.
  - `HybridLedger:Scheduler` ? `Interval`, `StartupDelay` (worker only).
  - Optional Cleanup (cloud):
    - `HybridLedger:Cleanup:Enabled` (true|false)
    - `HybridLedger:Cleanup:DeleteEntra` (true|false)
    - `HybridLedger:Cleanup:DeleteIntune` (true|false)
    - `HybridLedger:Cleanup:DryRun` (true|false; default true)
    - `HybridLedger:Cleanup:IntuneFreshWindowDays` (default 30)
    - Behavior: If AD object is stale/missing but Intune has a recent check-in within the freshness window, deletion is suppressed and an inconsistency is logged.
  - Optional SCCM cleanup:
    - `HybridLedger:Sccm:Enabled` (true|false)
    - `HybridLedger:Sccm:AdminServiceBaseUrl` (e.g., `https://cmserver.contoso.com/CCM_Proxy_ServerAuth/`)
    - `HybridLedger:Sccm:InactiveDaysThreshold` (default 90)
    - `HybridLedger:Sccm:ObsoleteDaysThreshold` (default 7)
- Set machine-level environment variables (examples):
  - `ASPNETCORE_ENVIRONMENT=Production`
  - `HYBRIDLEDGER__SNAPSHOT__ROOTPATH=C:\Apps\AdIntuneLedger\data`
  - `HYBRIDLEDGER__ACTIVEDIRECTORY__LDAPSERVER=dc1.contoso.com`
  - `HYBRIDLEDGER__ACTIVEDIRECTORY__USESSL=true`
  - `HYBRIDLEDGER__ACTIVEDIRECTORY__BASEDN=DC=contoso,DC=com`
  - `HYBRIDLEDGER__ACTIVEDIRECTORY__USERNAME=CONTOSO\svc-hybridledger`
  - `HYBRIDLEDGER__ACTIVEDIRECTORY__PASSWORD=...` (secure)
  - `HYBRIDLEDGER__GRAPH__TENANTID=00000000-0000-0000-0000-000000000000`
  - Optional Cleanup:
    - `HYBRIDLEDGER__CLEANUP__ENABLED=true`
    - `HYBRIDLEDGER__CLEANUP__DELETEENTRA=true`
    - `HYBRIDLEDGER__CLEANUP__DELETEINTUNE=false`
    - `HYBRIDLEDGER__CLEANUP__DRYRUN=true`
    - `HYBRIDLEDGER__CLEANUP__INTUNEFRESHWINDOWDAYS=30`
  - Optional SCCM:
    - `HYBRIDLEDGER__SCCM__ENABLED=true`
    - `HYBRIDLEDGER__SCCM__ADMINSERVICEBASEURL=https://cmserver.contoso.com/CCM_Proxy_ServerAuth/`

6) Install the worker as a Windows Service
- Ensure the worker is built with Windows Service hosting (the generic host with Windows Service integration).
- On the worker server, place files in `C:\Apps\AdIntuneLedger\Worker`.
- Create the service (example):
  - `sc create "AdIntuneLedger.Worker" binPath= "C:\Apps\AdIntuneLedger\Worker\AdIntuneLedger.Worker.exe" start= auto`
  - Set service logon: `sc config "AdIntuneLedger.Worker" obj= "CONTOSO\svc-hybridledger" password= "<secure>"`
  - Configure failure recovery (optional): `sc failure "AdIntuneLedger.Worker" reset= 86400 actions= restart/600/restart/600/restart/600`
- Start the service: `sc start "AdIntuneLedger.Worker"`

7) Deploy the web app to IIS
- Install IIS and the ASP.NET Core Hosting Bundle on the web server.
- Copy published files to `C:\Apps\AdIntuneLedger\Web`.
- Create an Application Pool:
  - .NET CLR: No Managed Code
  - 64-bit: Enabled
  - Identity: Custom (if you need UNC access to `data`) or `ApplicationPoolIdentity`.
- Create a Site:
  - Physical path: `C:\Apps\AdIntuneLedger\Web`
  - Binding: `https` on 443 with the server certificate.
  - Host name: your chosen DNS name.
- Grant the app pool identity permissions to read `Web` and read `data` (or modify if needed).
- Configure environment variables for the site (Configuration Editor or machine-level env): ensure `HYBRIDLEDGER__SNAPSHOT__ROOTPATH` matches the worker’s output location.
- Recycle the application pool or restart the site.

8) Firewall and TLS
- Web server: allow inbound TCP 443.
- Worker server: allow outbound TCP 443 to Microsoft Graph endpoints if used and to SCCM Admin Service if enabled.
- Ensure LDAPS trust: import required issuing CA certificates so LDAPS validation succeeds.

9) Validation checklist
- Worker:
  - Service status is Running.
  - `C:\Apps\AdIntuneLedger\data\latest-snapshot.json` appears after first run, and `logs` contains structured log files.
  - If Cleanup is enabled, logs show deletions or “would delete” and any inconsistencies.
  - If SCCM cleanup is enabled, logs show SCCM cleanup summaries (obsolete/inactive deleted counts).
- Web:
  - Browse `https://<hostname>/` to view the dashboard.
  - `https://<hostname>/api/snapshot` returns the current JSON.
- Cross-check counts vs. expectations and confirm timestamps are current.

10) Operations
- Updating:
  - Stop the Windows service and stop the IIS site.
  - Copy new published files to `Worker` and `Web` folders (preserve `appsettings.*.json`).
  - Start the service and start the site.
- Backup: keep `appsettings.*.json` and `data` safe; logs are typically transient.
- Troubleshooting:
  - Worker: Windows Event Viewer (Application) and `C:\Apps\AdIntuneLedger\logs`.
  - Web: IIS logs and application logs; check ANCM logs in the site’s `logs` if enabled.
  - Validate environment variables resolved by checking process environment and log output.

11) Alternative: single-server Kestrel (without IIS)
- Web:
  - Set `ASPNETCORE_URLS=https://+:5001` and install a certificate bound to port 5001.
  - Run the web app as its own Windows service or Scheduled Task if desired.
- Ensure firewall allows the chosen port and restrict access appropriately.

12) Uninstalling
- Worker: `sc stop "AdIntuneLedger.Worker"` then `sc delete "AdIntuneLedger.Worker"`.
- Web: remove the IIS site and application pool.

Notes
- Store secrets out of source control; prefer environment variables or a secure secret store available on servers.
- Use least-privilege for service and app pool identities.
- Ensure `data` is accessible to both components; use an absolute `HybridLedger:Snapshot:RootPath` to avoid working-directory issues.
- Cleanup and SCCM modules are modular; leave disabled if not required.
