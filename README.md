# AD-Intune Ledger

A .NET 8 solution for monitoring hybrid-joined Windows clients across on-premises Active Directory, Microsoft Entra ID, and Microsoft Intune. The background worker reconciles device inventories and produces structured JSON snapshots that power a lightweight Razor Pages dashboard.

## Solution structure

| Project | Description |
| --- | --- |
| `AdIntuneLedger.Core` | Domain models, configuration options, and abstractions shared across the solution. |
| `AdIntuneLedger.Infrastructure` | Implementations for Active Directory (LDAP), Microsoft Graph, reconciliation logic, and JSON snapshot storage. |
| `AdIntuneLedger.Worker` | Background worker service that executes the reconciliation pipeline on a configurable schedule and emits structured logs. |
| `AdIntuneLedger.Web` | Razor Pages dashboard and API endpoint for inspecting the latest reconciliation snapshot. |
| `AdIntuneLedger.Sccm` | Optional module for SCCM (ConfigMgr) cleanup through the Admin Service. |

## Prerequisites

- .NET 8 Runtime/SDK (preview channel). `Serilog`, `Azure.Identity`, and `Microsoft.Graph` are restored via NuGet.
- Network access to a domain controller supporting LDAP/LDAPS for Active Directory queries.
- A Microsoft Entra ID / Intune tenant with permissions to query Microsoft Graph. Ensure the identity used has sufficient permissions for any optional delete operations you enable.

## Configuration

Both the worker and web projects read a shared `HybridLedger` section (see `appsettings.json`). Key properties:

```json
"HybridLedger": {
  "ActiveDirectory": {
    "LdapServer": "dc1.contoso.com",
    "Port": 636,
    "UseSsl": true,
    "BaseDn": "DC=contoso,DC=com",
    "ActivityWindowDays": 30,
    "Username": null,
    "Password": null
  },
  "Graph": {
    "TenantId": "00000000-0000-0000-0000-000000000000",
    "Scopes": [ "https://graph.microsoft.com/.default" ]
  },
  "Snapshot": {
    "RootPath": "data",
    "LatestFileName": "latest-snapshot.json"
  },
  "Scheduler": {
    "Interval": "01:00:00",
    "StartupDelay": "00:00:10"
  },
  "Cleanup": {
    "Enabled": false,
    "DeleteEntra": false,
    "DeleteIntune": false,
    "DryRun": true,
    "IntuneFreshWindowDays": 30
  },
  "Sccm": {
    "Enabled": false,
    "AdminServiceBaseUrl": "https://cmserver.contoso.com/CCM_Proxy_ServerAuth/",
    "InactiveDaysThreshold": 90,
    "ObsoleteDaysThreshold": 7
  }
}
```

- Provide domain credentials via the Windows credential store, environment variables, or a secure secret provider; never hard-code sensitive values.
- `DefaultAzureCredential` respects managed identity, workload identity, Visual Studio/CLI credentials, etc. Ensure the identity has the necessary Microsoft Graph application permissions for any delete operations you enable.
- Cleanup module: when enabled, the worker deletes Entra/Intune devices whose AD counterparts are stale or missing, except when the Intune device has a recent check-in (within `IntuneFreshWindowDays`). In that case, no deletion occurs and an inconsistency is logged. Defaults to `DryRun=true`.
- The worker writes structured logs to `logs/worker-*.json` and snapshots to `data/latest-snapshot.json`. The web app reads snapshots from the same directory.
- SCCM cleanup is optional and disabled by default. Enable by setting `HybridLedger:Sccm:Enabled=true` and configuring `AdminServiceBaseUrl`.

## Running locally

1. Restore and build the solution:

   ```powershell
   dotnet build
   ```

2. Update `appsettings.Development.json` (worker & web) with environment-specific settings. Consider using [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) during development.

3. Start the worker to generate snapshots:

   ```powershell
   dotnet run --project src/AdIntuneLedger.Worker
   ```

4. Launch the web dashboard in a separate terminal:

   ```powershell
   dotnet run --project src/AdIntuneLedger.Web
   ```

5. Navigate to `https://localhost:5001` (or the configured URL) to review counts, gaps, and export JSON via `/api/snapshot`.

## Deployment

For step-by-step on-premises deployment (no Azure services), see `docs/DEPLOYMENT.md`. It covers installing the worker as a Windows Service and hosting the web app on IIS, configuration, permissions, firewall, validation, and operations, plus optional SCCM and cleanup configuration.

## Observability

- Structured JSON logs (Serilog) simplify ingestion into centralized logging.
- Each reconciliation persists a timestamped snapshot in `data/` for historical comparisons.
- The dashboard surfaces current counts and missing-device tables for quick remediation.

## Next steps

- Integrate a secure secret provider for LDAP/Graph credentials.
- Add alerting when discrepancies or cleanup inconsistencies are detected.
- Extend the dashboard with filtering and search capabilities.
- Publish containers or Windows services for production deployment.
