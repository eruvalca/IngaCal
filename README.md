# IngaCal

A personal time journal built with .NET 10, Blazor Interactive Server, the bundled Bootstrap 5.3.3, ASP.NET Core Identity, and EF Core SQLite.

## Run locally

Install the .NET 10 SDK, then from the repository root:

```powershell
dotnet restore IngaCal.sln
dotnet run --project IngaCal/IngaCal.csproj
```

Open the URL printed by the host and register. Registration signs in immediately; email delivery, confirmation, email changes, and password recovery are intentionally unavailable. Password changes, passkeys, two-factor authentication, and recovery codes remain in Account. Keep your password and 2FA recovery codes.

Development startup applies checked-in migrations. Before a pending migration touches an existing database, the app creates a consistent SQLite backup in the database directory's `backups` subdirectory. The original local `IngaCal/Data/app.db` is retained, but database files, sidecars, backups, and data-protection keys are excluded from Git and publishing.

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

Run **one application instance**. Put both SQLite and data-protection keys on persistent, writable storage **outside the deployment directory**. A relative SQLite path resolves against the application's content root; use an absolute production path to avoid deployment-directory ambiguity. The app creates parent directories and reports access failures at startup.

Example PowerShell configuration (replace the paths with your actual storage):

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ConnectionStrings__DefaultConnection = 'Data Source=D:/IngaCalData/journal.db'
$env:Storage__DataProtectionPath = 'D:/IngaCalData/keys'
dotnet publish IngaCal/IngaCal.csproj -c Release -o artifacts/publish
```

Stop the running app before deployment migrations. From the published directory, with the **same configuration** as the host:

```powershell
dotnet IngaCal.dll --migrate
dotnet IngaCal.dll
```

`--migrate` backs up an existing database if migrations are pending, applies them, and exits. Ordinary Production startup does not migrate automatically. Always check the migration command's exit code before starting the new version. The checked-in journal migration only creates Activities, Tags, and their association; it does not replace the Identity tables.

EF's SQLite provider enables WAL when creating a database. SQLite needs correct filesystem locking; WAL requires all processes to be on the same machine and **does not work on a network filesystem**. Do not assume an Azure App Service persistent mount is suitable merely because files survive restarts. Assess the actual App Service OS, mount type, locking guarantees, backup behavior, and single-instance configuration first. If the mount cannot satisfy SQLite requirements, choose a host with a suitable persistent local disk. A container's ephemeral writable layer is not persistent storage.

Keep Identity data-protection keys persistent with the database, and restrict OS access to the service account and administrators. Keys are not encrypted at rest by this app; use appropriate disk protection/OS permissions, or configure a platform-specific key encryptor before hosting. Losing keys invalidates protected cookies/tokens. Use HTTPS in production; passkeys require a secure context and stable relying-party domain. Configure forwarded headers and the reverse proxy according to your host rather than trusting arbitrary forwarded requests.

## Backup and restore

Use the SQLite online backup API through the app (works while a local instance is running):

```powershell
dotnet IngaCal.dll --backup D:/IngaCalBackups/journal-2026-09-28.db
```

Supply the same connection-string configuration as the host. The destination must not exist. This copies a consistent database snapshot, including committed WAL data. Also back up the data-protection key directory securely. Do not copy just an active `.db` file and ignore its WAL sidecar.

To restore: stop the app and all writers; retain the current database and its sidecars together as a rollback set; restore the backup to the configured database path without mixing it with old `-wal` or `-shm` files; restore the matching key directory; verify filesystem permissions; run `--migrate` with the intended app version; start the single instance and verify sign-in and reports. Rehearse restoration before relying on backups. Never copy restored data over a running database.

## Architecture and validation

Application routes use Interactive Server with prerendering disabled so browser time-zone detection precedes data loading. Identity pages use static SSR. Every service operation obtains ownership from authenticated server state and creates/disposes its own context through `IDbContextFactory<ApplicationDbContext>`. Activity overlap validation and writes share an immediate SQLite transaction. Application-managed version tokens detect stale edits.

The Blazor calendar adapter owns/disposes its JS module and callback reference. FullCalendar handles pointer interaction; all changes go through the same server-side validation as manual edits. Reports clip UTC intervals to local date boundaries, split elapsed time by local day, and expose chart values in tables.

See [test evidence and acceptance checks](docs/testing.md). Run the VSTest/xUnit suite:

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

Implementation references: [.NET 10 render modes](https://learn.microsoft.com/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0), [Blazor with EF Core](https://learn.microsoft.com/aspnet/core/blazor/blazor-ef-core?view=aspnetcore-10.0), [SQLite provider limitations](https://learn.microsoft.com/ef/core/providers/sqlite/limitations), [FullCalendar Bootstrap 5](https://fullcalendar.io/docs/bootstrap5), [FullCalendar time zones](https://fullcalendar.io/docs/timeZone), [FullCalendar license](https://fullcalendar.io/license), [BlazorExpress.ChartJS](https://www.nuget.org/packages/BlazorExpress.ChartJS/1.2.4), and [SQLite WAL filesystem requirements](https://www.sqlite.org/wal.html).
