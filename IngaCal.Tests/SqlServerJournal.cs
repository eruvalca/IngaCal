using IngaCal.Data;
using IngaCal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IngaCal.Tests;

internal sealed class SqlServerJournal(SqlServerFixture server) : IAsyncDisposable, IDbContextFactory<ApplicationDbContext>
{
    public const string Alice = "alice";
    public const string Bob = "bob";
    public static readonly DateTimeOffset Morning = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private readonly ServiceProvider applicationServices = new ServiceCollection()
        .Configure<IdentityOptions>(IdentityConfiguration.Configure)
        .BuildServiceProvider();
    public string DatabaseName { get; } = $"IngaCalTest_{Guid.NewGuid():N}";
    public string ConnectionString => new SqlConnectionStringBuilder(server.ConnectionString)
        { InitialCatalog = DatabaseName, Pooling = false }.ConnectionString;
    public DbContextOptions<ApplicationDbContext> Options => new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlServer(ConnectionString)
        .UseApplicationServiceProvider(applicationServices)
        .Options;

    public ApplicationDbContext CreateDbContext() => new(Options);
    public ActivityService Activities(string user = Alice) => new(this, new TestUser(user));
    public TagService Tags(string user = Alice) => new(this, new TestUser(user));
    public ReportService Reports(string user = Alice) => new(Activities(user));

    public static async Task<SqlServerJournal> CreateAsync(SqlServerFixture server)
    {
        var journal = new SqlServerJournal(server);
        try
        {
            await using var db = journal.CreateDbContext();
            await db.Database.MigrateAsync();
            db.Users.AddRange(new ApplicationUser { Id = Alice, UserName = Alice }, new ApplicationUser { Id = Bob, UserName = Bob });
            await db.SaveChangesAsync();
            return journal;
        }
        catch
        {
            await journal.DisposeAsync();
            throw;
        }
    }

    public static ActivityEdit Edit(DateTimeOffset? start = null, DateTimeOffset? end = null, IReadOnlyList<Guid>? tags = null,
        string title = "Focus", string description = "") => new(null, title, description, start ?? Morning, end ?? Morning.AddHours(1), tags ?? []);

    public static ActivityEdit Edit(ActivityDto item) => new(item.Id, item.Title, item.Description, item.Start, item.End, item.Tags.Select(x => x.Id).ToArray(), item.Version);
    public static TagEdit Edit(TagDto item) => new(item.Id, item.Name, item.Color, item.IsArchived, item.Version);

    public async ValueTask DisposeAsync()
    {
        try
        {
            // Only this fixture's generated database on the disposable container is deleted.
            await using var db = CreateDbContext();
            await db.Database.EnsureDeletedAsync();
        }
        finally { await applicationServices.DisposeAsync(); }
    }

    private sealed class TestUser(string id) : ICurrentUser
    {
        public Task<string> GetIdAsync() => Task.FromResult(id);
    }
}
