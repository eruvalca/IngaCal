using IngaCal.Services;

namespace IngaCal.Tests;

public sealed class ReportServiceTests
{
    private static readonly DateOnly Day = new(2026, 9, 21);
    private static readonly DateTimeOffset Midnight = new(2026, 9, 21, 0, 0, 0, TimeSpan.Zero);
    private static ReportFilter Filter(DateOnly? from = null, DateOnly? to = null, IReadOnlyList<Guid>? tags = null,
        TagMatch match = TagMatch.Any, bool untagged = false, ReportGrouping grouping = ReportGrouping.Daily, string zone = "UTC") =>
        new(from ?? Day, to ?? Day.AddDays(1), zone, tags ?? [], match, untagged, grouping);

    [Fact]
    public async Task Get_MultipleTagsClippingAndCrossMidnightSplits_CountsOverallOnceAndEachTagFully()
    {
        await using var journal = await SqliteJournal.CreateAsync();
        var work = await journal.Tags().SaveAsync(new(null, "Work", "#123456"));
        var learning = await journal.Tags().SaveAsync(new(null, "Learning", "#abcdef"));
        await journal.Activities().SaveAsync(SqliteJournal.Edit(Midnight.AddMinutes(-30), Midnight.AddMinutes(30), [work.Id, learning.Id]));
        await journal.Activities().SaveAsync(SqliteJournal.Edit(Midnight.AddHours(23.5), Midnight.AddHours(24.5), [work.Id, learning.Id]));
        await journal.Activities().SaveAsync(SqliteJournal.Edit(Midnight.AddHours(47.5), Midnight.AddHours(48.5)));
        // Archives preserve historical reporting.
        await journal.Tags().SaveAsync(SqliteJournal.Edit(learning) with { IsArchived = true });

        var result = await journal.Reports().GetAsync(Filter());

        Assert.Equal(120, result.TotalMinutes);
        Assert.Equal(60, result.AverageMinutes);
        Assert.Equal(30, result.UntaggedMinutes);
        Assert.Equal(3, result.ActivityCount);
        Assert.Equal(new[] { new TrendPoint(Day, 60), new TrendPoint(Day.AddDays(1), 60) }, result.Trend);
        Assert.Collection(result.Tags,
            tag => Assert.Equal(new TagTotal(learning.Id, "Learning", "#abcdef", 90, 2, 75), tag),
            tag => Assert.Equal(new TagTotal(work.Id, "Work", "#123456", 90, 2, 75), tag),
            tag => Assert.Equal(new TagTotal(Guid.Empty, "Untagged", "#8993a4", 30, 1, 25), tag));
        Assert.Equal(175, result.Tags.Sum(x => x.Percentage));
    }

    [Fact]
    public async Task Get_AnyAllAndUntaggedFilters_SelectMatchingActivitiesAndTagTotals()
    {
        await using var journal = await SqliteJournal.CreateAsync();
        var a = await journal.Tags().SaveAsync(new(null, "A", "#123456"));
        var b = await journal.Tags().SaveAsync(new(null, "B", "#abcdef"));
        var c = await journal.Tags().SaveAsync(new(null, "C", "#654321"));
        await journal.Activities().SaveAsync(SqliteJournal.Edit(Midnight, Midnight.AddHours(1), [a.Id, b.Id]));
        await journal.Activities().SaveAsync(SqliteJournal.Edit(Midnight.AddHours(1), Midnight.AddHours(1.5), [a.Id, c.Id]));
        await journal.Activities().SaveAsync(SqliteJournal.Edit(Midnight.AddHours(2), Midnight.AddHours(2.25), [b.Id]));
        await journal.Activities().SaveAsync(SqliteJournal.Edit(Midnight.AddHours(3), Midnight.AddHours(3.75)));

        var any = await journal.Reports().GetAsync(Filter(tags: [a.Id, b.Id]));
        var all = await journal.Reports().GetAsync(Filter(tags: [a.Id, b.Id], match: TagMatch.All));
        var untagged = await journal.Reports().GetAsync(Filter(tags: [a.Id], untagged: true));

        Assert.Equal(105, any.TotalMinutes);
        Assert.Equal(3, any.ActivityCount);
        Assert.Equal(new[] { a.Id, b.Id }, any.Tags.Select(x => x.Id));
        Assert.Equal(new double[] { 90, 75 }, any.Tags.Select(x => x.Minutes));
        Assert.Equal(0, any.UntaggedMinutes);
        Assert.Equal(60, all.TotalMinutes);
        Assert.Equal(1, all.ActivityCount);
        Assert.All(all.Tags, x => { Assert.Equal(60, x.Minutes); Assert.Equal(100, x.Percentage); });
        Assert.Equal(2, all.Tags.Count);
        Assert.Equal(45, untagged.TotalMinutes);
        Assert.Equal(45, untagged.UntaggedMinutes);
        Assert.Equal(new TagTotal(Guid.Empty, "Untagged", "#8993a4", 45, 1, 100), Assert.Single(untagged.Tags));
    }

    [Fact]
    public async Task Get_EmptyRange_ReturnsZeroMetricsAndEveryCalendarDay()
    {
        await using var journal = await SqliteJournal.CreateAsync();
        var report = await journal.Reports().GetAsync(Filter(to: Day.AddDays(2)));
        Assert.Equal(0, report.TotalMinutes);
        Assert.Equal(0, report.AverageMinutes);
        Assert.Equal(0, report.UntaggedMinutes);
        Assert.Equal(0, report.ActivityCount);
        Assert.Empty(report.Tags);
        Assert.Equal(new[] { new TrendPoint(Day, 0), new TrendPoint(Day.AddDays(1), 0), new TrendPoint(Day.AddDays(2), 0) }, report.Trend);
    }

    [Fact]
    public async Task Get_WeeklyGrouping_UsesMondayBucketsIncludingPartialAndEmptyWeeks()
    {
        await using var journal = await SqliteJournal.CreateAsync();
        // September 20 is Sunday; September 21 is Monday.
        await journal.Activities().SaveAsync(SqliteJournal.Edit(Midnight.AddHours(-1), Midnight.AddHours(1)));
        await journal.Activities().SaveAsync(SqliteJournal.Edit(Midnight.AddDays(2), Midnight.AddDays(2).AddMinutes(30)));
        var report = await journal.Reports().GetAsync(Filter(Day.AddDays(-1), Day.AddDays(8), grouping: ReportGrouping.Weekly));

        Assert.Equal(150, report.TotalMinutes);
        Assert.Equal(15, report.AverageMinutes);
        Assert.Equal(new[] { new TrendPoint(new(2026, 9, 14), 60), new TrendPoint(Day, 90), new TrendPoint(Day.AddDays(7), 0) }, report.Trend);
    }

    [Theory]
    [InlineData(3, 8, 1380)]
    [InlineData(11, 1, 1500)]
    public async Task Get_DaylightSavingDay_MeasuresActualElapsedMinutes(int month, int day, int expectedMinutes)
    {
        await using var journal = await SqliteJournal.CreateAsync();
        var date = new DateOnly(2026, month, day);
        var start = JournalTime.DayStart(date, "America/Chicago");
        var end = JournalTime.DayStart(date.AddDays(1), "America/Chicago");
        await journal.Activities().SaveAsync(SqliteJournal.Edit(start.AddHours(-1), end.AddHours(1)));

        var report = await journal.Reports().GetAsync(Filter(date, date, zone: "America/Chicago"));
        Assert.Equal(expectedMinutes, report.TotalMinutes);
        Assert.Equal(expectedMinutes, report.AverageMinutes);
        Assert.Equal(expectedMinutes, report.UntaggedMinutes);
        Assert.Equal(new TrendPoint(date, expectedMinutes), Assert.Single(report.Trend));
    }

    [Fact]
    public async Task Get_AccountIsolationAndForeignFilter_DoNotExposeOtherUsersTime()
    {
        await using var journal = await SqliteJournal.CreateAsync();
        var foreignTag = await journal.Tags(SqliteJournal.Bob).SaveAsync(new(null, "Private", "#123456"));
        await journal.Activities().SaveAsync(SqliteJournal.Edit());
        await journal.Activities(SqliteJournal.Bob).SaveAsync(SqliteJournal.Edit(end: SqliteJournal.Morning.AddHours(4), tags: [foreignTag.Id]));

        var own = await journal.Reports().GetAsync(Filter());
        var other = await journal.Reports(SqliteJournal.Bob).GetAsync(Filter());
        var forgedFilter = await journal.Reports().GetAsync(Filter(tags: [foreignTag.Id]));
        Assert.Equal(60, own.TotalMinutes);
        Assert.Equal("Untagged", Assert.Single(own.Tags).Name);
        Assert.Equal(240, other.TotalMinutes);
        Assert.Equal(foreignTag.Id, Assert.Single(other.Tags).Id);
        Assert.Equal(0, forgedFilter.TotalMinutes);
        Assert.Empty(forgedFilter.Tags);
    }

    [Fact]
    public async Task Get_InclusiveDates_UsesSelectedZoneAndIncludesEndDate()
    {
        await using var journal = await SqliteJournal.CreateAsync();
        var start = new DateTimeOffset(2026, 9, 22, 4, 30, 0, TimeSpan.Zero); // 23:30 September 21 Chicago.
        await journal.Activities().SaveAsync(SqliteJournal.Edit(start, start.AddHours(1)));
        var chicago = await journal.Reports().GetAsync(Filter(Day, Day, zone: "America/Chicago"));
        var utc = await journal.Reports().GetAsync(Filter(Day, Day));

        Assert.Equal(30, chicago.TotalMinutes);
        Assert.Equal(new TrendPoint(Day, 30), Assert.Single(chicago.Trend));
        Assert.Equal(0, utc.TotalMinutes);
        Assert.Equal(0, utc.ActivityCount);
    }

    [Fact]
    public async Task Get_InvalidDateRanges_ReturnActionableErrors()
    {
        await using var journal = await SqliteJournal.CreateAsync();
        var reversed = await Assert.ThrowsAsync<JournalException>(() => journal.Reports().GetAsync(Filter(Day, Day.AddDays(-1))));
        var maximum = await Assert.ThrowsAsync<JournalException>(() => journal.Reports().GetAsync(Filter(DateOnly.MaxValue, DateOnly.MaxValue)));
        Assert.Contains("on or after", reversed.Message);
        Assert.Contains("9999", maximum.Message);
    }
}
