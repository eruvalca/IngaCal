# Repository Guidelines

## Project Structure & Architecture

- `IngaCal/` hosts the .NET 10 Blazor application. Application pages use Interactive Server without prerendering; Identity pages use static SSR.
- `Components/` groups pages, layouts, calendar, reports, and account UI. Keep JavaScript modules beside their components as `ComponentName.razor.js`.
- `Services/` contains journal operations, DTOs, reporting, and time-zone logic; `Data/` contains entities, SQLite configuration, and EF Core migrations.
- `wwwroot/` contains styling and local vendor assets. Retain bundled Bootstrap and vendor license notices.
- `IngaCal.Tests/` contains .NET tests; `scripts/tests/` contains JavaScript tests. See `README.md` for deployment and `docs/testing.md` for acceptance checks.

## Build, Test, and Development Commands

Run from the repository root with the .NET 10 SDK; JavaScript tests also require Node.js:

```powershell
dotnet restore IngaCal.sln                         # Restore dependencies
dotnet build IngaCal.sln -c Release               # Build both projects
dotnet run --project IngaCal/IngaCal.csproj        # Start local development
dotnet test IngaCal.Tests/IngaCal.Tests.csproj     # Run xUnit/bUnit tests
node --test scripts/tests/report-chart-labels.test.mjs
dotnet publish IngaCal/IngaCal.csproj -c Release -o artifacts/publish
```

Development startup applies migrations. Use isolated database and key paths for previews.

## Coding Style & Naming Conventions

Use four-space indentation, file-scoped C# namespaces, PascalCase types/components/public members, camelCase locals/private fields, and `Async` suffixes for asynchronous methods. Preserve nullable annotations. Match surrounding Razor and JavaScript formatting; no repository-specific formatter or linter is configured.

Keep components focused and validation in services. Obtain ownership from authenticated server state and dispose a fresh `IDbContextFactory<ApplicationDbContext>` context per operation. Initialize JavaScript after rendering and dispose interop resources.

## Testing Guidelines

Use xUnit with VSTest, bUnit for components, real temporary SQLite databases for persistence, and Node's built-in test runner for chart JavaScript. Name tests `Operation_Scenario_ExpectedBehavior` in `*Tests.cs`. No numeric coverage threshold is configured. Cover changed behavior, especially account isolation, overlaps, stale edits, DST, and report denominators. Verify visual changes on desktop/mobile and light/dark themes.

## Commit & Pull Request Guidelines

History uses plain descriptive messages, such as `Build personal time journal with calendar, tags, and reporting`; follow that imperative style. Keep commits focused. PRs should explain behavior, include validation results, link relevant issues, and provide screenshots for UI changes. Describe migration/storage impacts when applicable.

## Data & Configuration Safety

Never commit or publish live databases, sidecars, backups, or data-protection keys. Configure `ConnectionStrings__DefaultConnection` and `Storage__DataProtectionPath` for isolated previews or persistent production storage. Preserve existing data; follow the README's backup and explicit production migration procedure. Keep UTC storage and existing elapsed-time/report-counting semantics intact.
