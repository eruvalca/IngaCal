# IngaCal

A personal time journal built with .NET 10, Blazor Interactive Server, the bundled Bootstrap 5.3.3, ASP.NET Core Identity, and EF Core SQL Server.

## Run locally

Install the .NET 10 SDK and Docker Desktop with Linux containers (x64). Allocate at least 4 GB of memory to Docker for SQL Server and the test runner. The app runs on the host; only SQL Server runs in Compose.

From the repository root:

```powershell
Copy-Item .env.example .env
# Edit .env: set MSSQL_SA_PASSWORD to a strong, unique local password.
# Single-quote its value if it contains a dollar sign or other Compose interpolation characters.
docker compose up -d --wait
dotnet user-secrets set 'ConnectionStrings:DefaultConnection' 'Server=tcp:127.0.0.1,14333;Database=IngaCalDev;User ID=sa;Password=<same password as .env>;Encrypt=True;TrustServerCertificate=True' --project IngaCal/IngaCal.csproj
dotnet restore IngaCal.sln
dotnet run --project IngaCal/IngaCal.csproj
```

Replace the password placeholder before running the secrets command. For a password containing connection-string delimiters, quote its value according to SQL Server connection-string syntax. `.env` is ignored by Git and configures **Compose only**; it does not configure the host application. User secrets configure the app and EF tooling. Alternatively supply `ConnectionStrings__DefaultConnection` in the application's environment. Never commit credentials. `sa` is used only for this local development instance, including creating/migrating the development database.

Compose uses SQL Server 2025 Enterprise Developer edition, binds only to `127.0.0.1:14333`, and checks readiness with `sqlcmd`. The development connection trusts the container's self-signed certificate. To change the host port, change `MSSQL_PORT` in `.env` and the connection string together. The image tag follows servicing updates within SQL Server 2025; pull deliberately when updating your local server.

Open the URL printed by the host and register. Registration signs in immediately; email delivery, confirmation, email changes, and password recovery are intentionally unavailable. Password changes, passkeys, two-factor authentication, and recovery codes remain in Account. Keep your password and 2FA recovery codes.

Development startup creates the database and applies checked-in migrations. SQL Server data survives container replacement in the Compose named volume. ASP.NET Core manages Data Protection keys using the host's defaults; on a Windows developer account this normally uses `%LOCALAPPDATA%/ASP.NET/DataProtection-Keys` with Windows encryption. For an isolated preview, use a different database name and `DataProtection__ApplicationName`; `Storage__DataProtectionPath` can optionally select a separate key directory. User secrets are shared by checkouts with the same project secret ID.

EF Core is configured directly in `Program.cs` with `GetConnectionString("DefaultConnection")`, `AddDbContextFactory<ApplicationDbContext>`, and `UseSqlServer`. Runtime startup and the design-time factory share a connection-string validator. Data Protection is independent of EF Core. Switching from the previous `Data/keys` configuration to platform defaults can require signing in again; old keys are left untouched. Set the directory override to the previous location if you need to retain that key ring.

## Planned Azure hosting

The intended starting point is **Azure App Service with the built-in .NET 10 runtime and Azure SQL Database**, using managed identity for database access. See [the Azure deployment guide](docs/azure-deployment.md) for exact application settings, database permissions, Blazor WebSockets/affinity, key persistence, migrations, and backup/restore. No Azure resources have been provisioned or cloud deployment verified yet.

`/health` is an anonymous, read-only EF Core database connectivity probe. It returns 200 when the configured database is reachable and 503 when it is unavailable. It does not create the database, apply migrations, or return connection details.

```powershell
docker compose stop                 # Pause SQL Server, retaining data
docker compose up -d --wait         # Start again
docker compose down                 # Remove the container, retaining the volume
# DESTRUCTIVE: explicitly discard this Compose project's development SQL Server data:
docker compose down --volumes
```

Changing `.env` does not change the `sa` password in an existing volume. Change it in SQL Server, or deliberately reset an expendable development volume. Back up anything you need before a reset.

This version starts with a **new SQL Server database**. Existing SQLite databases, sidecars, and keys are left untouched and excluded from publishing. SQLite migrations are retained in Git history; this version does not import or open SQLite databases. Accounts must be registered again in the new database.

## Recording and exploring time

- Calendar opens on today in your device's time zone. Select a time range, move an activity, or resize either end. Dragging uses 15-minute increments; the editor accepts exact minutes and explicit dates for overnight entries. Moving preserves elapsed duration.
- Give an activity a title, description, or both. Add any number of tags, or leave it untagged. Copy opens an editable draft for tomorrow; change its dates before saving if desired.
- Adjacent activities are allowed; overlaps are rejected for your account. Conflicting edits from another tab never silently overwrite newer data. Close and reopen the editor to refresh, or use Retry after a failed drag.
- Tags can be renamed, recolored, archived, and restored. Archived tags stay on history and in reports; restore them before assigning them to new activities.
- Reports start with the last seven calendar days. Date ranges include both endpoint dates. Choose Any/All tag matching, Untagged only, or daily/weekly trends. Each tag gets the full duration of a matching activity. Overall totals count it once, so tag percentages can exceed 100% in total. Tag filters limit the tag bars, pie chart, and table to the chosen tags and the overall/trend values to matching activities. The pie compares tag-counted hours, including untagged time, with a color legend showing each duration and percentage; its slices and hover labels show shares of the sum of tag totals, not unique logged time. Percentages are rounded to one decimal place. Weekly rows start Monday and include only dates inside the selected range.
- Bars and pie slices display duration and percentage labels without hovering. Bar percentages match the table's share of logged time; pie percentages use the sum of the displayed tag totals. Small pie slices use callout lines, and charts grow vertically when there are more tags to keep labels readable.
- The device zone is checked again when the browser regains focus. If detection fails, choose a zone manually. Times in a spring clock-change gap are rejected; the editor provides a second-occurrence checkbox during repeated autumn hours. UTC instants and actual elapsed durations are preserved.
- Choose system, light, or dark appearance. Every activity can be edited without dragging. The expandable activity list and report tables provide keyboard-accessible alternatives.

## Production storage and migration

Provision SQL Server or Azure SQL separately; the Compose Developer edition is for development/testing, not production licensing or deployment. Use the host's persistent Data Protection key store; if you override it, keep keys on persistent, writable storage **outside the deployment directory**, restricted to the service account and administrators. Azure App Service provides default persistence within a deployment slot; see the Azure guide for slot swaps and other Azure hosts.

Example Windows configuration using the application's Windows service identity:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ConnectionStrings__DefaultConnection = 'Server=tcp:sql.example.com,1433;Database=IngaCal;Integrated Security=True;Encrypt=True;TrustServerCertificate=False'
$env:Storage__DataProtectionPath = 'D:/IngaCalData/keys'
dotnet publish IngaCal/IngaCal.csproj -c Release -o artifacts/publish
```

Use your host's supported authentication mechanism and secret store. SQL Server's certificate must chain to a trusted authority and match the connection hostname. Do not copy the development `TrustServerCertificate=True` setting into production. Give the runtime login only the necessary application data permissions, not `sa`, database creation, or schema-owner privileges.

Before deploying, stop the app's writers and verify a recoverable database backup plus recovery access to its Data Protection keys. Use native SQL Server backup tooling for SQL Server, or Azure SQL Database's managed backups and point-in-time restore. Run migrations with a separate deployment identity authorized to change the schema. Pre-provision the Azure SQL database; for SQL Server, an identity creating a database also needs database-creation rights. From the published directory, configure the migration identity's connection string to the **same target database**, then:

```powershell
dotnet IngaCal.dll --migrate
if ($LASTEXITCODE -ne 0) { throw 'Migration failed; do not start the new application.' }
# Restore the runtime identity's connection-string configuration before starting the host.
dotnet IngaCal.dll
```

`--migrate` applies migrations and exits. It does **not** take a backup. Ordinary Production startup never migrates automatically. The initial SQL Server migration creates the complete Identity version 3 and journal schema in a fresh database. It cannot upgrade or transfer an existing SQLite database. Retain the old app/database as a separate rollback set during the provider transition.

For EF development tooling, restore the pinned local tool and configure the same connection-string secret/environment setting:

```powershell
dotnet tool restore
dotnet ef migrations add DescribeChange --project IngaCal/IngaCal.csproj
dotnet ef migrations script --idempotent --project IngaCal/IngaCal.csproj --output artifacts/migration.sql
```

The design-time context uses the same Identity schema options as the app and tests. Review generated migrations before deployment. Use a single app instance unless you also configure Blazor routing and shared data-protection storage for a multi-instance deployment; SQL Server locking alone does not configure those hosting requirements.

Key encryption at rest follows the host defaults. Windows profile storage uses DPAPI; App Service's default key store does not add Data Protection key encryption. An explicit filesystem override also disables automatic key encryption; use a platform-appropriate encryptor when required. Losing keys invalidates protected cookies/tokens. Use HTTPS in production; passkeys require a secure context and stable relying-party domain. Configure forwarded headers and the reverse proxy according to your host rather than trusting arbitrary forwarded requests.

## Backup and restore

For **Azure SQL Database**, use managed backups and point-in-time restore as described in [the Azure guide](docs/azure-deployment.md); the server-file commands below do not apply to that service.

For **SQL Server**, use native backup tooling under a database administrator or backup identity. The app's old `--backup` command now fails with guidance. For example, execute this T-SQL through your SQL administration tool:

```sql
BACKUP DATABASE [IngaCal]
TO DISK = N'/var/opt/mssql/backup/IngaCal-20260928.bak'
WITH COPY_ONLY, CHECKSUM;

RESTORE VERIFYONLY
FROM DISK = N'/var/opt/mssql/backup/IngaCal-20260928.bak'
WITH CHECKSUM;
```

Choose a new filename for every backup. Paths refer to the **SQL Server machine/container**, not the application host. Create the backup directory with SQL Server service-account write access. For a local container, copy backups off its volume before deleting that volume. Keep secure off-server copies and back up data-protection keys separately. A named volume is persistence, not a backup. Production backup scheduling, retention, and transaction-log backups should match the chosen recovery model and recovery objectives.

Rehearse restores into a **new database**: inspect logical files with `RESTORE FILELISTONLY`, then use `RESTORE DATABASE` with `MOVE` for each file to new server-side paths. Verify sign-in, activities, tags, and reports with an isolated app instance and restored key copy. Before an actual cutover, stop all writers, preserve the current database and keys as a rollback set, restore to the intended target, run the intended version's `--migrate`, and switch the connection string only after validation. Do not overwrite a running database. `RESTORE VERIFYONLY` supplements but does not replace a rehearsed restore.

## Architecture and validation

Application routes use Interactive Server with prerendering disabled so browser time-zone detection precedes data loading. Identity pages use static SSR. Every service operation obtains ownership from authenticated server state and creates/disposes its own context through `IDbContextFactory<ApplicationDbContext>`. Activity saves acquire a transaction-owned SQL Server application lock per authenticated account before overlap validation, using read-committed isolation and a 10-second lock timeout. Commit/rollback releases the lock, including when validation or cancellation fails. The provider uses its default execution strategy without automatic transaction replay. Application-managed version tokens detect stale edits.

The Blazor calendar adapter owns/disposes its JS module and callback reference. FullCalendar handles pointer interaction; all changes go through the same server-side validation as manual edits. Reports clip UTC intervals to local date boundaries, split elapsed time by local day, and expose chart values in tables.

See [test evidence and acceptance checks](docs/testing.md). With Docker running, run the VSTest/xUnit suite (Testcontainers starts an independent disposable SQL Server; Compose does not need to be running):

```powershell
dotnet test IngaCal.Tests/IngaCal.Tests.csproj
```

No cloud deployment, recurrence, timers, offline editing, external calendar integrations, or report downloads are included.

## Local frontend assets

All browser scripts/styles/fonts are served locally. Pinned dependencies:

| Dependency | Version | License |
|---|---|---|
| Bundled Bootstrap | 5.3.3 | MIT |
| FullCalendar Standard and Bootstrap 5 theme | 7.1.0 | MIT |
| Bootstrap Icons | 1.13.1 | MIT |
| BlazorExpress.ChartJS | 1.2.4 | Apache-2.0 |
| Chart.js | 4.4.1 | MIT |
| Chart.js DataLabels | 2.2.0 | MIT |

FullCalendar Standard needs no signup or license key. License notices are retained alongside vendored assets in `wwwroot/lib`; the BlazorExpress package supplies its static assets via NuGet. `scripts/Get-ClientAssets.ps1` reproducibly downloads the pinned vendor assets if they need replacing. Bootstrap remains the template's existing local copy.

Implementation references: [.NET 10 render modes](https://learn.microsoft.com/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0), [Blazor with EF Core](https://learn.microsoft.com/aspnet/core/blazor/blazor-ef-core?view=aspnetcore-10.0), [SQL Server provider](https://learn.microsoft.com/ef/core/providers/sql-server/), [FullCalendar Bootstrap 5](https://fullcalendar.io/docs/bootstrap5), [FullCalendar time zones](https://fullcalendar.io/docs/timeZone), [FullCalendar license](https://fullcalendar.io/license), [BlazorExpress.ChartJS](https://www.nuget.org/packages/BlazorExpress.ChartJS/1.2.4), and [SQL Server containers](https://learn.microsoft.com/sql/linux/quickstart-install-connect-docker).
