# Entra ID app registrations for NimbusLedger

This guide creates two separate Entra ID app registrations:
- Worker app (daemon) for Microsoft Graph using application permissions.
- Web app (Razor Pages) for user sign-in with OpenID Connect.

Use separate registrations for least privilege and simpler admin consent.

## Prerequisites
- Tenant admin to grant application permissions (Graph).
- Web app URLs for redirect/sign-out in each environment (dev/prod).
- Replace placeholders like <tenant-guid>, <tenant-domain>, <app-client-id>, <https-url>.

---
## 1) Worker app (daemon) — Microsoft Graph application permissions

Purpose: the background worker reconciles devices using client credentials flow and Graph application permissions.

Steps
1. Register a new app
   - Name: NimbusLedger Worker
   - Supported account types: Single tenant
   - Redirect URIs: none required
2. Certificates & secrets
   - Prefer an X.509 certificate (upload public cert)
   - Fallback: create a client secret and note its Value and expiry
3. API permissions (Microsoft Graph ? Application permissions)
   - Read-only (recommended baseline):
     - Device.Read.All
     - DeviceManagementManagedDevices.Read.All
     - Directory.Read.All (commonly required for directory reads)
   - If Cleanup.DeleteEntra = true: Device.ReadWrite.All
   - If Cleanup.DeleteIntune = true: DeviceManagementManagedDevices.ReadWrite.All
   - Click “Grant admin consent”
4. Optional: Managed identity alternative
   - If deploying the worker with a managed identity, assign the same Graph app roles to that identity via Enterprise applications. Skip client secret/cert in that case.

Worker configuration mapping
- Environment variables (recommended):
  - AZURE_TENANT_ID = <tenant-guid>
  - AZURE_CLIENT_ID = <worker-app-client-id>
  - AZURE_CLIENT_SECRET = <secret> (if using client secret)
  - Or configure certificate (thumbprint/path) per your hosting option
- App settings (already present):
  - HybridLedger:Graph:TenantId = <tenant-guid>
  - HybridLedger:Graph:Scopes = [ "https://graph.microsoft.com/.default" ]

Notes
- Use only the permissions you need. Omit write permissions unless cleanup is enabled.
- Rotate secrets regularly. Prefer certificates for long-lived automation.

---
## 2) Web app (Razor Pages) — user sign-in with OpenID Connect

Purpose: protect the dashboard with Entra ID sign-in. The web app does not call Graph by default.

Steps
1. Register a new app
   - Name: NimbusLedger Web
   - Supported account types: Single tenant
2. Platform configuration ? Add a platform ? Web
   - Redirect URIs (add one per environment):
     - Dev: https://localhost:<port>/signin-oidc
     - Prod: <https-url>/signin-oidc
   - Front-channel logout URL (optional):
     - <https-url>/signout-callback-oidc
3. Certificates & secrets
   - Create a client secret and note its Value
   - Required for authorization code flow used by server-side Razor Pages
4. Token configuration
   - ID tokens enabled (default for Web)
   - Optional: add group claims if you plan to authorize by group membership
5. Restrict who can sign in (optional)
   - Enterprise applications ? NimbusLedger Web ? Properties ? set “User assignment required” = Yes
   - Assign allowed users/groups in “Users and groups”

Web configuration mapping (NimbusLedger.Web)
- In appsettings.json (placeholders must be replaced):
  - AzureAd.Instance = https://login.microsoftonline.com/
  - AzureAd.Domain = <tenant-domain>
  - AzureAd.TenantId = <tenant-guid>
  - AzureAd.ClientId = <web-app-client-id>
  - AzureAd.CallbackPath = /signin-oidc
- Store the web app ClientSecret securely (do not commit):
  - For local dev, use user secrets:
    - dotnet user-secrets set "AzureAd:ClientSecret" "<secret-value>"
  - For production, use a secure secret store (e.g., Key Vault, environment variables)

Behavior in the app
- The site requires authentication by default; `/Error` and `/Privacy` are anonymous.
- `/api/snapshot` requires authentication.
- Sign-in via `/signin`, sign-out via `/signout`.

---
## Security recommendations
- Least privilege: only include write permissions when cleanup is enabled.
- Prefer certificates/managed identity over client secrets.
- Do not store secrets in source control. Use user secrets or a vault.
- Rotate secrets regularly and monitor sign-in/logs.
