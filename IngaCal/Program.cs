using IngaCal.Components;
using IngaCal.Components.Account;
using IngaCal.Data;
using IngaCal.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var migrate = args.Contains("--migrate");
var backupIndex = Array.IndexOf(args, "--backup");
var backupPath = backupIndex >= 0 && backupIndex + 1 < args.Length ? args[backupIndex + 1] : null;
if (backupIndex >= 0 && backupPath is null) throw new ArgumentException("Use --backup <destination.db>.");
var hostArgs = args.Where((x, i) => x != "--migrate" && i != backupIndex && (backupIndex < 0 || i != backupIndex + 1)).ToArray();
var builder = WebApplication.CreateBuilder(hostArgs);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
}).AddIdentityCookies();
builder.Services.AddAuthorization();
var storage = DatabaseStorage.Configure(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddDbContextFactory<ApplicationDbContext>(options => options.UseSqlite(storage.ConnectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddDataProtection().SetApplicationName("IngaCal").PersistKeysToFileSystem(new DirectoryInfo(storage.KeyDirectory));
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.User.RequireUniqueEmail = true;
    options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
}).AddEntityFrameworkStores<ApplicationDbContext>().AddSignInManager().AddDefaultTokenProviders();

builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IActivityService, ActivityService>();
builder.Services.AddScoped<ITagService, TagService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<BrowserContext>();
var app = builder.Build();
if (backupPath is not null)
{
    DatabaseStorage.Backup(storage.ConnectionString, Path.GetFullPath(backupPath));
    return;
}
if (migrate || app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if ((await db.Database.GetPendingMigrationsAsync()).Any())
    {
        if (File.Exists(storage.DatabasePath) && new FileInfo(storage.DatabasePath).Length > 0)
            DatabaseStorage.Backup(storage.ConnectionString, Path.Combine(Path.GetDirectoryName(storage.DatabasePath)!, "backups", $"before-migration-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db"));
        await db.Database.MigrateAsync();
    }
    if (migrate) return;
}
if (app.Environment.IsDevelopment()) app.UseMigrationsEndPoint();
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapAdditionalIdentityEndpoints();
app.Run();
