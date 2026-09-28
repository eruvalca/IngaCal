# Azure deployment preparation

Target Azure App Service's built-in .NET 10 runtime with Azure SQL Database. The app runs directly on App Service; the repository's SQL Server Compose service remains a development dependency. Azure resources, identities, and role assignments are managed separately from the repository. Validate each release against the deployed app before inviting users.

## App Service settings

Start with one instance and choose a region close to the Azure SQL database. Publish **only** `IngaCal/IngaCal.csproj` in Release and deploy its publish output, not a solution-level publish containing the test project. Built-in Windows hosting supplies IIS integration; Linux hosting also needs the HTTPS forwarding configuration below. Confirm .NET 10 runtime availability on the selected App Service stack at deployment time.

The initial deployment uses the **Windows F1 Free** App Service plan in Central US, SQL server `ingacal.database.windows.net`, database `ingacaldb` (Basic), and web app `ingacal` at `ingacal-drejfqenf6gcb6dz.centralus-01.azurewebsites.net`. The GitHub Actions workflow publishes on pushes to `main` and deploys that artifact using its separate GitHub deployment identity. F1 supports WebSockets with a limit of five concurrent connections per instance, but does **not** support Always On: expect cold starts after inactivity and respect Free CPU/bandwidth quotas. Upgrade to Basic or higher when these constraints are unacceptable.

Set these application settings through App Service configuration (or infrastructure as code when provisioning is introduced):

| Setting | Value |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__DefaultConnection` | The managed-identity connection string below |
| `DataProtection__ApplicationName` | `IngaCal-Production`; keep stable across releases |
| `Storage__DataProtectionPath` | Leave unset to use platform key storage |
| `AllowedHosts` | The app's actual public hostnames, separated by semicolons |

Use a separate database and `DataProtection__ApplicationName` for an independent test environment. Choose application settings consistently; if using App Service's **Connection strings** section instead, use the name `DefaultConnection` and type `SQLAzure`, and avoid defining a conflicting app setting.

Enable **HTTPS Only**, **WebSockets**, and **session/ARR affinity**. Enable **Always On** only on a plan that supports it (not F1). Keep App Service Authentication (Easy Auth) disabled for the initial deployment: the application already owns authentication through ASP.NET Core Identity. Probe `/health` directly; if the plan supports the App Service Health check feature, configure it to use `/health`. It permits anonymous requests and returns only the standard health status: 200 when EF can connect to SQL, 503 when it cannot. It does not validate schema currency, apply migrations, or exercise sign-in.

For **Linux App Service**, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` only behind the managed App Service ingress, following Microsoft's cloud-hosting guidance. This forwards the original HTTPS scheme before HTTPS redirection; the switch relaxes proxy-address checks and must not be copied to an app exposed directly to untrusted traffic. For another reverse proxy, configure its specific trusted proxies/networks instead. Validate HTTPS redirects and secure cookies through the public hostname after deployment. Windows IIS hosting supplies this integration automatically.

Interactive Server circuits live in the app process. Deployment/restart can require reconnecting or reloading; persistent authentication keys do not preserve circuits. Before scaling out, load-test affinity and connection limits. Azure SignalR Service is an optional later scaling choice, with server-sticky routing for Blazor; it is not needed for the initial deployment. Use a stable HTTPS hostname before registering passkeys.

Sources: [Blazor on App Service](https://learn.microsoft.com/aspnet/core/blazor/host-and-deploy/server/?view=aspnetcore-10.0#azure-app-service), [forwarded headers](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0#forward-the-scheme-for-linux-and-non-iis-reverse-proxies), [App Service health checks](https://learn.microsoft.com/azure/app-service/monitor-instances-health-check), [App Service Free limits](https://learn.microsoft.com/azure/azure-resource-manager/management/azure-subscription-service-limits#azure-app-service-limits).

## Azure SQL and managed identity

Enable the App Service's system-assigned managed identity. Configure a Microsoft Entra administrator on the Azure SQL logical server, then create a database user for that identity in the **application database**, using an authorized Entra administrator/deployment identity:

```sql
-- Replace with the managed identity's name; disambiguate duplicate Entra names if necessary.
CREATE USER [ingacal] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [ingacal];
ALTER ROLE db_datawriter ADD MEMBER [ingacal];
```

This baseline grants application data access, not schema ownership. The user-assigned identity `ingacal-id-af2a` is for GitHub Actions deployment; the web app itself uses its **system-assigned** identity to connect to SQL. The existing application lock uses the database's `public` principal. Verify its execution under the actual runtime identity. On F1, allow the app's current outbound IPs in SQL server firewall rules (recheck after changing tiers or moving the app); remove the broad **Allow Azure services and resources to access this server** (`0.0.0.0`) rule. Higher tiers can use VNet integration with an Azure SQL private endpoint and private DNS. Retain a narrow temporary administrator IP rule only while it is needed.

Set the runtime connection string to:

```text
Server=tcp:ingacal.database.windows.net,1433;Database=ingacaldb;Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
```

There is no SQL password. For a user-assigned managed identity, assign it to the app and add `User ID=<managed-identity-client-id>` to the connection string. Local development continues using the container and user secrets; the production identity is not required to run locally.

The existing EF Core SQL Server provider supports Azure SQL. Its resolved SqlClient 6.1.6 dependency includes managed-identity authentication. Recheck authentication dependencies when upgrading SqlClient: version 7 separates Entra authentication into `Microsoft.Data.SqlClient.Extensions.Azure`.

Keep the existing `UseSqlServer` registration and execution strategy for now. Do not blindly switch to `UseAzureSql` or add `EnableRetryOnFailure`: automatic execution retries require redesigning the journal's explicit transaction/application-lock operation as a replay-safe unit, including ambiguous commits. Azure transient failures currently surface as failed operations; validate that behavior in staging before launch and design any automatic retry policy deliberately.

Azure SQL defaults to `READ_COMMITTED_SNAPSHOT ON`. The concurrent-overlap regression runs with this setting both on and off, verifying one save and one overlap rejection. Local SQL Server tests do not replace validation on Azure SQL itself.

Sources: [managed identity from App Service](https://learn.microsoft.com/azure/app-service/tutorial-connect-msi-sql-database), [SqlClient Entra authentication](https://learn.microsoft.com/sql/connect/ado-net/sql/azure-active-directory-authentication), [read-committed snapshot semantics](https://learn.microsoft.com/sql/t-sql/statements/set-transaction-isolation-level-transact-sql#arguments), [EF connection resiliency](https://learn.microsoft.com/ef/core/miscellaneous/connection-resiliency).

## Data Protection keys and deployment slots

With `Storage__DataProtectionPath` unset, ASP.NET Core chooses its host-specific key store. On App Service, keys live under `%HOME%/ASP.NET/DataProtection-Keys`, outside the deployed application. App Service shares that storage between instances **within one deployment slot**. Keep `DataProtection__ApplicationName` stable so releases can read the same cookies and tokens.

The default App Service key store has two relevant limits: slots use different key rings, and Data Protection does not encrypt those key files at rest. For an initial single-slot deployment, the platform provides persistence without custom application storage code. Do not point keys at the deployment directory, which may be read-only with run-from-package deployment or replaced by publishing.

Before using slot swaps while preserving sign-in, or moving to Azure Container Apps/multiple independent hosts, configure **Azure Blob Storage for the shared key ring and Azure Key Vault for key encryption**, accessed with managed identity. That integration is not installed in this change because those resources and the deployment topology have not been chosen. Use the current `Azure.Extensions.AspNetCore.DataProtection.Blobs`, `Azure.Extensions.AspNetCore.DataProtection.Keys`, and `Azure.Identity` packages when implementing it. Key Vault protects the ring; it does not replace Blob Storage as its repository.

Use narrowly scoped `Storage Blob Data Contributor` access on the key container and `Key Vault Crypto User` (or an equivalent least-privilege custom role) on the wrapping key. Retain old wrapping-key versions so older Data Protection keys remain readable. Production deployment slots that must preserve cookies need the same key ring and application name, compatible Identity/cookie settings, and coordinated access to the intended database. An independent test environment must not share the production key ring/application name. Plan slot-sticky connection settings and managed-identity grants together and rehearse a swap.

`Storage__DataProtectionPath` remains an escape hatch for an explicitly mounted persistent directory or local preview. Setting it overrides platform storage and automatic at-rest encryption. Existing `IngaCal/Data/keys` files are never deleted or imported automatically. Changing stores invalidates old cookies/tokens unless the original ring is retained and used.

Sources: [default key management](https://learn.microsoft.com/aspnet/core/security/data-protection/configuration/default-settings?view=aspnetcore-10.0), [Blob key storage](https://learn.microsoft.com/aspnet/core/security/data-protection/implementation/key-storage-providers?view=aspnetcore-10.0#azure-storage), [Key Vault encryption](https://learn.microsoft.com/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0#protect-keys-with-azure-key-vault-protectkeyswithazurekeyvault).

## Migrations, backup, and release checks

Provision the Azure SQL database before running the application. Use a separate deployment identity with schema-change permissions for migrations; do not grant the runtime identity `db_owner`. Use an authenticated deployment job and its own connection string, or a reviewed EF migration script/bundle. Production startup never migrates automatically, and `/health` never creates or changes the schema.

Before a migration, stop writers or use an explicitly reviewed backwards-compatible rollout. Confirm Azure SQL backup retention and point-in-time recovery availability, record the pre-migration recovery time, and rehearse restore to a **new database**. If a discrete pre-release recovery copy is needed, create and verify a database copy with Azure tooling. Azure SQL Database manages backups; the README's SQL Server `BACKUP DATABASE ... TO DISK` commands do not apply to Azure SQL Database. Database recovery also needs access to the correct Data Protection key ring.

For the explicit migration command, provide the deployment identity's configuration to the published app:

```powershell
dotnet IngaCal.dll --migrate
if ($LASTEXITCODE -ne 0) { throw 'Migration failed; do not start the release.' }
```

Restore/use the runtime identity's configuration for normal hosting. App Service's startup command must not contain `--migrate`.

Before the first production release, verify in Azure staging:

1. Runtime managed-identity SQL access, private DNS/network access, and successful health probes. Denied SQL access must make `/health` return 503 without disclosing connection details.
2. Registration, login/logout, antiforgery-protected forms, journal edits and concurrent overlap rejection, reports, passkeys/2FA on the intended hostname, and data/key persistence across app restart and redeployment.
3. HTTPS forwarding, WebSockets, reconnect behavior, affinity, and any planned scale-out/slot swap. Check logs for ephemeral-key or key-decryption warnings.
4. Migration permissions under the separate deployment identity, a rehearsed database/key recovery path, and the chosen response to transient Azure SQL failures.
5. Check sign-out returns to the sign-in page without an error; the previously recorded `~//` redirect issue has been corrected.

Source: [Azure SQL automated backups and restore](https://learn.microsoft.com/azure/azure-sql/database/automated-backups-overview?view=azuresql).
