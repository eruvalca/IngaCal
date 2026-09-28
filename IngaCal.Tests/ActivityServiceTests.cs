using System.Security.Cryptography;
using System.Text;
using IngaCal.Services;
using Microsoft.EntityFrameworkCore;

namespace IngaCal.Tests;

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Database")]
public sealed class ActivityServiceTests(SqlServerFixture server)
{
    [Fact]
    public async Task Save_ConcurrentAdjacentWrites_BothSucceed()
    {
        await using var journal = await SqlServerJournal.CreateAsync(server);
        var start = SqlServerJournal.Morning;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<ActivityDto> Write(int hour)
        {
            await gate.Task;
            return await journal.Activities().SaveAsync(SqlServerJournal.Edit(start.AddHours(hour), start.AddHours(hour + 1)));
        }
        var first = Write(0);
        var second = Write(1);
        gate.SetResult();
        var results = await Task.WhenAll(first, second);
        Assert.NotEqual(results[0].Id, results[1].Id);
        Assert.Equal(2, (await journal.Activities().ListAsync(start, start.AddDays(1))).Count);
    }

    [Fact]
    public async Task Save_LockedAccount_TimesOutWithoutWritingAndOtherAccountCanSave()
    {
        await using var journal = await SqlServerJournal.CreateAsync(server);
        await using var blocker = journal.CreateDbContext();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await HoldAliceLockAsync(blocker);

        var bob = await journal.Activities(SqlServerJournal.Bob).SaveAsync(SqlServerJournal.Edit(title: "Other account"));
        var error = await Assert.ThrowsAsync<JournalException>(() => journal.Activities().SaveAsync(SqlServerJournal.Edit()));
        Assert.Contains("try again", error.Message);
        Assert.Empty(await journal.Activities().ListAsync(SqlServerJournal.Morning, SqlServerJournal.Morning.AddDays(1)));
        Assert.Equal(bob.Id, Assert.Single(await journal.Activities(SqlServerJournal.Bob).ListAsync(bob.Start, bob.End)).Id);

        await transaction.RollbackAsync();
        var saved = await journal.Activities().SaveAsync(SqlServerJournal.Edit());
        Assert.Equal(saved.Id, Assert.Single(await journal.Activities().ListAsync(saved.Start, saved.End)).Id);
    }

    [Fact]
    public async Task Save_CancelledWhileWaitingForLock_DoesNotWriteAndReleasesTransaction()
    {
        await using var journal = await SqlServerJournal.CreateAsync(server);
        await using var blocker = journal.CreateDbContext();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await HoldAliceLockAsync(blocker);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => journal.Activities().SaveAsync(SqlServerJournal.Edit(), cancellation.Token));
        Assert.Empty(await journal.Activities().ListAsync(SqlServerJournal.Morning, SqlServerJournal.Morning.AddDays(1)));
        await transaction.RollbackAsync();
        var saved = await journal.Activities().SaveAsync(SqlServerJournal.Edit());
        Assert.Equal(saved.Id, Assert.Single(await journal.Activities().ListAsync(saved.Start, saved.End)).Id);
    }

    private static Task HoldAliceLockAsync(IngaCal.Data.ApplicationDbContext db)
    {
        var resource = "IngaCal:Activity:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SqlServerJournal.Alice)));
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0;
            IF @result < 0 THROW 50000, 'Could not acquire test lock', 1;
            """);
    }

    [Fact]
    public async Task Crud_IsolatedByAccount_EvenWithForgedIdentifiers()
    {
        await using var journal = await SqlServerJournal.CreateAsync(server);
        var alice = journal.Activities();
        var bob = journal.Activities(SqlServerJournal.Bob);
        var own = await alice.SaveAsync(SqlServerJournal.Edit(title: "Alice's time"));
        var other = await bob.SaveAsync(SqlServerJournal.Edit(title: "Bob's time"));

        Assert.Equal(own.Id, Assert.Single(await alice.ListAsync(own.Start, own.End)).Id);
        Assert.Equal(other.Id, Assert.Single(await bob.ListAsync(own.Start, own.End)).Id);
        var saveError = await Assert.ThrowsAsync<JournalException>(() => bob.SaveAsync(SqlServerJournal.Edit(own) with { Title = "Forged" }));
        Assert.Contains("no longer available", saveError.Message);
        await Assert.ThrowsAsync<JournalException>(() => bob.DeleteAsync(own.Id, own.Version));

        var updated = await alice.SaveAsync(SqlServerJournal.Edit(own) with { Title = "Reviewed", End = own.End.AddMinutes(7) });
        Assert.Equal("Reviewed", updated.Title);
        Assert.Equal(67, updated.Minutes);
        await alice.DeleteAsync(updated.Id, updated.Version);
        Assert.Empty(await alice.ListAsync(own.Start, updated.End));
        Assert.Equal("Bob's time", Assert.Single(await bob.ListAsync(own.Start, updated.End)).Title);
    }

    [Theory]
    [InlineData(-60, 0, true)]
    [InlineData(60, 120, true)]
    [InlineData(-60, 1, false)]
    [InlineData(59, 120, false)]
    [InlineData(0, 60, false)]
    [InlineData(1, 59, false)]
    [InlineData(-1, 61, false)]
    public async Task Save_OverlapBoundaries_AllowOnlyAdjacentIntervals(int from, int to, bool succeeds)
    {
        await using var journal = await SqlServerJournal.CreateAsync(server);
        var service = journal.Activities();
        await service.SaveAsync(SqlServerJournal.Edit());
        var candidate = SqlServerJournal.Edit(SqlServerJournal.Morning.AddMinutes(from), SqlServerJournal.Morning.AddMinutes(to));

        if (succeeds)
        {
            await service.SaveAsync(candidate);
            Assert.Equal(2, (await service.ListAsync(SqlServerJournal.Morning.AddDays(-1), SqlServerJournal.Morning.AddDays(1))).Count);
        }
        else
        {
            var error = await Assert.ThrowsAsync<JournalException>(() => service.SaveAsync(candidate));
            Assert.Contains("overlaps", error.Message);
            Assert.Single(await service.ListAsync(SqlServerJournal.Morning.AddDays(-1), SqlServerJournal.Morning.AddDays(1)));
        }
    }

    [Fact]
    public async Task Save_CrossMidnightConflict_RejectsOverlapAndKeepsExactMinuteDuration()
    {
        await using var journal = await SqlServerJournal.CreateAsync(server);
        var start = new DateTimeOffset(2026, 9, 21, 23, 53, 0, TimeSpan.FromHours(-5));
        var saved = await journal.Activities().SaveAsync(SqlServerJournal.Edit(start, start.AddMinutes(22)));

        Assert.Equal(22, saved.Minutes);
        Assert.Equal(start.ToUniversalTime(), saved.Start);
        var error = await Assert.ThrowsAsync<JournalException>(() => journal.Activities().SaveAsync(SqlServerJournal.Edit(start.AddMinutes(10), start.AddMinutes(30))));
        Assert.Contains("overlaps", error.Message);
        Assert.Equal(saved.Id, Assert.Single(await journal.Activities().ListAsync(start, start.AddDays(1))).Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_ConcurrentConflictingWrites_OnlyOneSucceeds(bool readCommittedSnapshot)
    {
        await using var journal = await SqlServerJournal.CreateAsync(server);
        // Azure SQL enables RCSI by default. Verify serialization with either database setting.
        await using (var db = journal.CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync(readCommittedSnapshot
                ? "ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON"
                : "ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT OFF");
            var actual = await db.Database.SqlQueryRaw<bool>(
                "SELECT is_read_committed_snapshot_on AS [Value] FROM sys.databases WHERE name = DB_NAME()")
                .SingleAsync();
            Assert.Equal(readCommittedSnapshot, actual);
        }
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> Write(string title)
        {
            await gate.Task;
            return await Record.ExceptionAsync(() => journal.Activities().SaveAsync(SqlServerJournal.Edit(title: title)));
        }
        var firstTab = Task.Run(() => Write("First tab"));
        var secondTab = Task.Run(() => Write("Second tab"));
        gate.SetResult();
        var outcomes = await Task.WhenAll(firstTab, secondTab);

        Assert.Single(outcomes, x => x is null);
        var rejected = Assert.IsType<JournalException>(Assert.Single(outcomes, x => x is not null));
        Assert.Contains("overlaps", rejected.Message);
        Assert.Single(await journal.Activities().ListAsync(SqlServerJournal.Morning, SqlServerJournal.Morning.AddDays(1)));
    }

    [Fact]
    public async Task SaveAndDelete_StaleVersion_PreserveLatestActivity()
    {
        await using var journal = await SqlServerJournal.CreateAsync(server);
        var service = journal.Activities();
        var original = await service.SaveAsync(SqlServerJournal.Edit());
        var latest = await service.SaveAsync(SqlServerJournal.Edit(original) with { Title = "Latest", End = original.End.AddMinutes(13) });

        Assert.NotEqual(original.Version, latest.Version);
        var saveError = await Assert.ThrowsAsync<JournalException>(() => service.SaveAsync(SqlServerJournal.Edit(original) with { Title = "Stale" }));
        Assert.Contains("changed in another tab", saveError.Message);
        var deleteError = await Assert.ThrowsAsync<JournalException>(() => service.DeleteAsync(original.Id, original.Version));
        Assert.Contains("changed or was deleted", deleteError.Message);
        var remaining = Assert.Single(await service.ListAsync(original.Start, latest.End));
        Assert.Equal("Latest", remaining.Title);
        Assert.Equal(73, remaining.Minutes);
        Assert.Equal(latest.Version, remaining.Version);
    }

    [Fact]
    public async Task Save_ConflictingMove_PreservesOriginalTimeTextTagsAndVersion()
    {
        await using var journal = await SqlServerJournal.CreateAsync(server);
        var tag = await journal.Tags().SaveAsync(new(null, "Keep", "#123456"));
        var service = journal.Activities();
        var original = await service.SaveAsync(SqlServerJournal.Edit(tags: [tag.Id], title: "Original", description: "Notes"));
        await service.SaveAsync(SqlServerJournal.Edit(SqlServerJournal.Morning.AddHours(2), SqlServerJournal.Morning.AddHours(3)));

        var error = await Assert.ThrowsAsync<JournalException>(() => service.SaveAsync(SqlServerJournal.Edit(original) with
        {
            Start = original.Start.AddHours(1.5), End = original.End.AddHours(1.5), Title = "Move", Description = "Changed", TagIds = []
        }));

        Assert.Contains("overlaps", error.Message);
        var unchanged = Assert.Single(await service.ListAsync(original.Start, original.End));
        Assert.Equal(original.Start, unchanged.Start);
        Assert.Equal(original.End, unchanged.End);
        Assert.Equal("Original", unchanged.Title);
        Assert.Equal("Notes", unchanged.Description);
        Assert.Equal(original.Version, unchanged.Version);
        Assert.Equal(tag.Id, Assert.Single(unchanged.Tags).Id);
    }

    [Fact]
    public async Task Save_CancelledOperation_DoesNotPersistActivity()
    {
        await using var journal = await SqlServerJournal.CreateAsync(server);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => journal.Activities().SaveAsync(SqlServerJournal.Edit(), cancellation.Token));
        Assert.Empty(await journal.Activities().ListAsync(SqlServerJournal.Morning, SqlServerJournal.Morning.AddDays(1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_ForeignOrMissingTags_RejectsEntireWrite(bool missing)
    {
        await using var journal = await SqlServerJournal.CreateAsync(server);
        var ownTag = await journal.Tags().SaveAsync(new(null, "Own", "#123456"));
        var foreign = await journal.Tags(SqlServerJournal.Bob).SaveAsync(new(null, "Private", "#654321"));
        var forbiddenId = missing ? Guid.NewGuid() : foreign.Id;

        var error = await Assert.ThrowsAsync<JournalException>(() => journal.Activities().SaveAsync(SqlServerJournal.Edit(tags: [ownTag.Id, forbiddenId])));
        Assert.Contains("tag", error.Message);
        Assert.Empty(await journal.Activities().ListAsync(SqlServerJournal.Morning, SqlServerJournal.Morning.AddDays(1)));
        await using var db = journal.CreateDbContext();
        Assert.Empty(await db.ActivityTags.ToListAsync());
    }

    [Fact]
    public async Task Save_ArchivedTags_CanRetainOrRemoveButCannotNewlyAttach()
    {
        await using var journal = await SqlServerJournal.CreateAsync(server);
        var tag = await journal.Tags().SaveAsync(new(null, "History", "#123456"));
        var original = await journal.Activities().SaveAsync(SqlServerJournal.Edit(tags: [tag.Id]));
        await journal.Tags().SaveAsync(SqlServerJournal.Edit(tag) with { IsArchived = true });

        var retained = await journal.Activities().SaveAsync(SqlServerJournal.Edit(original) with { Title = "Still tagged" });
        Assert.True(Assert.Single(retained.Tags).IsArchived);
        var rejected = await Assert.ThrowsAsync<JournalException>(() => journal.Activities().SaveAsync(SqlServerJournal.Edit(SqlServerJournal.Morning.AddHours(2), SqlServerJournal.Morning.AddHours(3), [tag.Id])));
        Assert.Contains("Restore archived tags", rejected.Message);
        var removed = await journal.Activities().SaveAsync(SqlServerJournal.Edit(retained) with { TagIds = [] });
        Assert.Empty(removed.Tags);
        Assert.Single(await journal.Activities().ListAsync(SqlServerJournal.Morning, SqlServerJournal.Morning.AddDays(1)));
    }

    [Fact]
    public async Task Save_UpdatesTagSet_TrimsTextAndNormalizesUtc()
    {
        await using var journal = await SqlServerJournal.CreateAsync(server);
        var first = await journal.Tags().SaveAsync(new(null, "First", "#123456"));
        var second = await journal.Tags().SaveAsync(new(null, "Second", "#654321"));
        var third = await journal.Tags().SaveAsync(new(null, "Third", "#abcdef"));
        var start = new DateTimeOffset(2026, 9, 21, 9, 7, 0, TimeSpan.FromHours(-5));
        var saved = await journal.Activities().SaveAsync(SqlServerJournal.Edit(start, start.AddMinutes(19), [first.Id, second.Id, first.Id], "  Focus  ", "  Notes  "));
        var updated = await journal.Activities().SaveAsync(SqlServerJournal.Edit(saved) with { TagIds = [second.Id, third.Id, third.Id] });
        var reloaded = Assert.Single(await journal.Activities().ListAsync(start, start.AddMinutes(19)));

        Assert.Equal("Focus", reloaded.Title);
        Assert.Equal("Notes", reloaded.Description);
        Assert.Equal(TimeSpan.Zero, reloaded.Start.Offset);
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 14, 7, 0, TimeSpan.Zero), reloaded.Start);
        Assert.Equal(19, reloaded.Minutes);
        Assert.Equal(new[] { second.Id, third.Id }, reloaded.Tags.Select(x => x.Id));
        await using var db = journal.CreateDbContext();
        Assert.Equal(2, await db.ActivityTags.CountAsync());
        Assert.Equal(DateTimeKind.Utc, (await db.Activities.SingleAsync()).StartUtc.Kind);
        Assert.Equal(updated.Version, reloaded.Version);
    }

    [Fact]
    public async Task List_RangeBoundaries_ExcludesTouchingAndOrdersIntersectingActivities()
    {
        await using var journal = await SqlServerJournal.CreateAsync(server);
        var service = journal.Activities();
        var from = SqlServerJournal.Morning;
        await service.SaveAsync(SqlServerJournal.Edit(from.AddMinutes(20), from.AddMinutes(40), title: "Second"));
        await service.SaveAsync(SqlServerJournal.Edit(from.AddMinutes(-20), from, title: "Ends at start"));
        await service.SaveAsync(SqlServerJournal.Edit(from.AddMinutes(40), from.AddMinutes(60), title: "Starts at end"));
        await service.SaveAsync(SqlServerJournal.Edit(from, from.AddMinutes(20), title: "First"));

        var selected = await service.ListAsync(from, from.AddMinutes(40));
        Assert.Equal(new[] { "First", "Second" }, selected.Select(x => x.Title));
    }

    [Theory]
    [InlineData("blank", "title or a description")]
    [InlineData("long-title", "200 characters")]
    [InlineData("long-description", "4,000")]
    [InlineData("zero-duration", "after start")]
    [InlineData("negative-duration", "after start")]
    [InlineData("start-seconds", "whole minutes")]
    [InlineData("end-seconds", "whole minutes")]
    public async Task Save_InvalidInput_IsRejectedWithoutPersisting(string kind, string expectedError)
    {
        await using var journal = await SqlServerJournal.CreateAsync(server);
        var edit = SqlServerJournal.Edit();
        edit = kind switch
        {
            "blank" => edit with { Title = "  ", Description = "\t\n" },
            "long-title" => edit with { Title = new string('a', 201) },
            "long-description" => edit with { Description = new string('a', 4001) },
            "zero-duration" => edit with { End = edit.Start },
            "negative-duration" => edit with { End = edit.Start.AddMinutes(-1) },
            "start-seconds" => edit with { Start = edit.Start.AddSeconds(1) },
            "end-seconds" => edit with { End = edit.End.AddSeconds(1) },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        var error = await Assert.ThrowsAsync<JournalException>(() => journal.Activities().SaveAsync(edit));
        Assert.Contains(expectedError, error.Message);
        Assert.Empty(await journal.Activities().ListAsync(SqlServerJournal.Morning.AddDays(-1), SqlServerJournal.Morning.AddDays(1)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Save_TitleOrDescriptionAlone_AndMaximumLengthsAreAccepted(bool titleOnly)
    {
        await using var journal = await SqlServerJournal.CreateAsync(server);
        var saved = await journal.Activities().SaveAsync(SqlServerJournal.Edit(title: titleOnly ? new string('a', 200) : "", description: titleOnly ? "" : new string('b', 4000)));

        Assert.Equal(titleOnly ? 200 : 0, saved.Title.Length);
        Assert.Equal(titleOnly ? 0 : 4000, saved.Description.Length);
        Assert.Equal(titleOnly ? new string('a', 200) : new string('b', 4000), saved.DisplayTitle);
    }
}
