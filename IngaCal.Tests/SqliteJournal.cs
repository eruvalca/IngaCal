using IngaCal.Data;
using IngaCal.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IngaCal.Tests;

internal sealed class SqliteJournal : IAsyncDisposable, IDbContextFactory<ApplicationDbContext>
{
    public const string Alice = "alice";
    public const string Bob = "bob";
    public static readonly DateTimeOffset Morning = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private readonly ServiceProvider applicationServices = new ServiceCollection()
        .Configure<IdentityOptions>(options => options.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
        .BuildServiceProvider();
    public string DatabasePath { get; } = Path.Combine(Path.GetTempPath(), $"ingacal-test-{Guid.NewGuid():N}.db");
    public DbContextOptions<ApplicationDbContext> Options => new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite($"Data Source={DatabasePath};Pooling=False;Default Timeout=10")
        .UseApplicationServiceProvider(applicationServices)
        .Options;

    public ApplicationDbContext CreateDbContext() => new(Options);
    public ActivityService Activities(string user = Alice) => new(this, new TestUser(user));
    public TagService Tags(string user = Alice) => new(this, new TestUser(user));
    public ReportService Reports(string user = Alice) => new(Activities(user));

    public static async Task<SqliteJournal> CreateAsync()
    {
        var journal = new SqliteJournal();
        await using var db = journal.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        db.Users.AddRange(new ApplicationUser { Id = Alice, UserName = Alice }, new ApplicationUser { Id = Bob, UserName = Bob });
        await db.SaveChangesAsync();
        return journal;
    }

    public static ActivityEdit Edit(DateTimeOffset? start = null, DateTimeOffset? end = null, IReadOnlyList<Guid>? tags = null,
        string title = "Focus", string description = "") => new(null, title, description, start ?? Morning, end ?? Morning.AddHours(1), tags ?? []);

    public static ActivityEdit Edit(ActivityDto item) => new(item.Id, item.Title, item.Description, item.Start, item.End, item.Tags.Select(x => x.Id).ToArray(), item.Version);
    public static TagEdit Edit(TagDto item) => new(item.Id, item.Name, item.Color, item.IsArchived, item.Version);

    public async ValueTask DisposeAsync()
    {
        // These exact files belong to this fixture; no recursive or shared-directory cleanup.
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" }) File.Delete(DatabasePath + suffix);
        await applicationServices.DisposeAsync();
    }

    private sealed class TestUser(string id) : ICurrentUser
    {
        public Task<string> GetIdAsync() => Task.FromResult(id);
    }
}
