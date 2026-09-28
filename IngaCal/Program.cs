using IngaCal.Components;
using IngaCal.Components.Account;
using IngaCal.Data;
using IngaCal.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var migrate = args.Contains("--migrate");
if (args.Any(x => x == "--backup" || x.StartsWith("--backup=", StringComparison.Ordinal)))
    throw new ArgumentException("--backup is no longer supported. Use SQL Server BACKUP DATABASE and back up the data-protection keys; see README.md.");
var hostArgs = args.Where(x => x != "--migrate").ToArray();
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
var connectionString = SqlServerConnectionString.Validate(builder.Configuration.GetConnectionString("DefaultConnection"));
builder.Services.AddDbContextFactory<ApplicationDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>();
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName(builder.Configuration["DataProtection:ApplicationName"] ?? "IngaCal");
// Use the host's persistent key store by default (including Azure App Service).
// An explicit directory remains available for isolated previews and other hosts.
if (builder.Configuration["Storage:DataProtectionPath"] is { Length: > 0 } keyPath)
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(Path.GetFullPath(keyPath, builder.Environment.ContentRootPath)));
builder.Services.AddIdentityCore<ApplicationUser>(IdentityConfiguration.Configure)
    .AddEntityFrameworkStores<ApplicationDbContext>().AddSignInManager().AddDefaultTokenProviders();

builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IActivityService, ActivityService>();
builder.Services.AddScoped<ITagService, TagService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<BrowserContext>();
var app = builder.Build();
if (migrate || app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
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
app.MapHealthChecks("/health").AllowAnonymous();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapAdditionalIdentityEndpoints();
app.Run();
