using IngaCal.Services;

namespace IngaCal.Tests;

public sealed class TagServiceTests
{
    [Fact]
    public async Task List_OrdersNamesAndIncludesArchivedHistory_OnlyForCurrentAccount()
    {
        await using var journal = await SqliteJournal.CreateAsync();
        await journal.Tags().SaveAsync(new(null, "Work", "#123456"));
        await journal.Tags().SaveAsync(new(null, "Books", "#abcdef"));
        await journal.Tags().SaveAsync(new(null, "Archived", "#654321", IsArchived: true));
        await journal.Tags(SqliteJournal.Bob).SaveAsync(new(null, "Other account", "#000000"));

        var tags = await journal.Tags().ListAsync();
        Assert.Equal(new[] { "Archived", "Books", "Work" }, tags.Select(x => x.Name));
        Assert.True(tags[0].IsArchived);
        Assert.All(tags.Skip(1), x => Assert.False(x.IsArchived));
    }

    [Fact]
    public async Task Save_NormalizesUniqueNamePerAccount_IncludingArchivedNames()
    {
        await using var journal = await SqliteJournal.CreateAsync();
        var tag = await journal.Tags().SaveAsync(new(null, "  Reading  ", "#123456"));
        await journal.Tags().SaveAsync(SqliteJournal.Edit(tag) with { IsArchived = true });
        var conflict = await Assert.ThrowsAsync<JournalException>(() => journal.Tags().SaveAsync(new(null, "reading", "#abcdef")));
        var other = await journal.Tags(SqliteJournal.Bob).SaveAsync(new(null, "reading", "#abcdef"));

        Assert.Contains("already have a tag", conflict.Message);
        var own = Assert.Single(await journal.Tags().ListAsync());
        Assert.Equal("Reading", own.Name);
        Assert.True(own.IsArchived);
        Assert.Equal(other.Id, Assert.Single(await journal.Tags(SqliteJournal.Bob).ListAsync()).Id);
    }

    [Fact]
    public async Task Save_ArchiveRenameRecolorRestore_UpdatesHistoricalActivities()
    {
        await using var journal = await SqliteJournal.CreateAsync();
        var tag = await journal.Tags().SaveAsync(new(null, "Old", "#123456"));
        await journal.Activities().SaveAsync(SqliteJournal.Edit(tags: [tag.Id]));
        var archived = await journal.Tags().SaveAsync(SqliteJournal.Edit(tag) with { Name = "  Books  ", Color = "#abcdef", IsArchived = true });
        var history = Assert.Single(await journal.Activities().ListAsync(SqliteJournal.Morning, SqliteJournal.Morning.AddDays(1)));

        var historicalTag = Assert.Single(history.Tags);
        Assert.Equal("Books", historicalTag.Name);
        Assert.Equal("#abcdef", historicalTag.Color);
        Assert.True(historicalTag.IsArchived);
        Assert.NotEqual(tag.Version, archived.Version);
        var restored = await journal.Tags().SaveAsync(SqliteJournal.Edit(archived) with { IsArchived = false });
        Assert.False(restored.IsArchived);
        var newActivity = await journal.Activities().SaveAsync(SqliteJournal.Edit(SqliteJournal.Morning.AddHours(2), SqliteJournal.Morning.AddHours(3), [restored.Id]));
        Assert.Equal(restored.Id, Assert.Single(newActivity.Tags).Id);
    }

    [Fact]
    public async Task Save_StaleAndForeignEditsRejected_PreserveLatestTag()
    {
        await using var journal = await SqliteJournal.CreateAsync();
        var original = await journal.Tags().SaveAsync(new(null, "Original", "#123456"));
        var latest = await journal.Tags().SaveAsync(SqliteJournal.Edit(original) with { Name = "Latest", Color = "#abcdef" });
        var stale = await Assert.ThrowsAsync<JournalException>(() => journal.Tags().SaveAsync(SqliteJournal.Edit(original) with { IsArchived = true }));
        var foreign = await Assert.ThrowsAsync<JournalException>(() => journal.Tags(SqliteJournal.Bob).SaveAsync(SqliteJournal.Edit(latest) with { Name = "Forged" }));

        Assert.Contains("changed in another tab", stale.Message);
        Assert.Contains("no longer available", foreign.Message);
        Assert.Equal(latest, Assert.Single(await journal.Tags().ListAsync()));
        Assert.Empty(await journal.Tags(SqliteJournal.Bob).ListAsync());
    }

    [Fact]
    public async Task Save_RenameToDuplicateName_RejectsAndPreservesOriginalTag()
    {
        await using var journal = await SqliteJournal.CreateAsync();
        await journal.Tags().SaveAsync(new(null, "Taken", "#123456"));
        var other = await journal.Tags().SaveAsync(new(null, "Keep", "#abcdef"));

        var error = await Assert.ThrowsAsync<JournalException>(() => journal.Tags().SaveAsync(SqliteJournal.Edit(other) with { Name = "TAKEN", Color = "#000000", IsArchived = true }));
        Assert.Contains("already have a tag", error.Message);
        var unchanged = (await journal.Tags().ListAsync()).Single(x => x.Id == other.Id);
        Assert.Equal("Keep", unchanged.Name);
        Assert.Equal("#abcdef", unchanged.Color);
        Assert.False(unchanged.IsArchived);
        Assert.Equal(other.Version, unchanged.Version);
    }

    [Theory]
    [InlineData("", "#123456", "Tag names")]
    [InlineData("   ", "#123456", "Tag names")]
    [InlineData("name", "red", "valid tag color")]
    [InlineData("name", "#12345g", "valid tag color")]
    [InlineData("name", "#12345", "valid tag color")]
    [InlineData("name", "#1234567", "valid tag color")]
    [InlineData("name", "#123456\n", "valid tag color")]
    public async Task Save_InvalidInputRejected_WithoutPersisting(string name, string color, string expectedError)
    {
        await using var journal = await SqliteJournal.CreateAsync();
        var error = await Assert.ThrowsAsync<JournalException>(() => journal.Tags().SaveAsync(new(null, name, color)));
        Assert.Contains(expectedError, error.Message);
        Assert.Empty(await journal.Tags().ListAsync());
    }

    [Fact]
    public async Task Save_NameLengthBoundary_AcceptsSixtyAndRejectsSixtyOne()
    {
        await using var journal = await SqliteJournal.CreateAsync();
        var accepted = await journal.Tags().SaveAsync(new(null, new string('a', 60), "#AbCdEf"));
        var error = await Assert.ThrowsAsync<JournalException>(() => journal.Tags().SaveAsync(new(null, new string('b', 61), "#AbCdEf")));
        Assert.Contains("60 characters", error.Message);
        Assert.Equal(accepted, Assert.Single(await journal.Tags().ListAsync()));
    }
}
