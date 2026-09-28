using IngaCal.Services;

namespace IngaCal.Tests;

public sealed class ContractsTests
{
    [Theory]
    [InlineData("Title", "Description\nsecond line", "Title")]
    [InlineData("", "Description\nsecond line", "Description")]
    [InlineData(" ", "Only description", "Only description")]
    public void DisplayTitle_UsesTitleOrFirstDescriptionLine(string title, string description, string expected)
    {
        var item = new ActivityDto(Guid.NewGuid(), title, description, SqliteJournal.Morning, SqliteJournal.Morning.AddMinutes(17), Guid.NewGuid(), []);
        Assert.Equal(expected, item.DisplayTitle);
        Assert.Equal(17, item.Minutes);
    }

    [Theory]
    [InlineData(0, "0m")]
    [InlineData(17, "17m")]
    [InlineData(59, "59m")]
    [InlineData(59.6, "1h 00m")]
    [InlineData(61, "1h 01m")]
    [InlineData(125, "2h 05m")]
    public void DurationText_FormatsMinuteAndHourBoundaries(double minutes, string expected)
    {
        Assert.Equal(expected, DurationText.Format(minutes));
    }
}
