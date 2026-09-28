using IngaCal.Services;

namespace IngaCal.Tests;

public sealed class JournalTimeTests
{
    [Fact]
    public void Resolve_RepeatedHour_DistinguishesEarlierAndLaterOccurrence()
    {
        var repeated = new DateTime(2026, 11, 1, 1, 30, 0);
        var earlier = JournalTime.Resolve(repeated, "America/Chicago");
        var later = JournalTime.Resolve(repeated, "America/Chicago", laterOccurrence: true);

        Assert.Equal(new[] { TimeSpan.FromHours(-5), TimeSpan.FromHours(-6) }, JournalTime.Offsets(repeated, "America/Chicago"));
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero), earlier.ToUniversalTime());
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 7, 30, 0, TimeSpan.Zero), later.ToUniversalTime());
        Assert.Equal(TimeSpan.FromHours(1), later - earlier);
        Assert.Equal(repeated, JournalTime.Local(earlier, "America/Chicago"));
        Assert.Equal(repeated, JournalTime.Local(later, "America/Chicago"));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(2, 30)]
    [InlineData(2, 59)]
    public void Resolve_NonexistentSpringTime_RejectsWithExplanation(int hour, int minute)
    {
        var error = Assert.Throws<JournalException>(() => JournalTime.Resolve(new(2026, 3, 8, hour, minute, 0), "America/Chicago"));
        Assert.Contains("does not exist", error.Message);
    }

    [Fact]
    public void Resolve_SpringBoundary_PreservesActualOneMinuteBetweenLocal0159And0300()
    {
        var before = JournalTime.Resolve(new(2026, 3, 8, 1, 59, 0), "America/Chicago");
        var after = JournalTime.Resolve(new(2026, 3, 8, 3, 0, 0), "America/Chicago");
        Assert.Equal(TimeSpan.FromMinutes(1), after - before);
        Assert.Empty(JournalTime.Offsets(new(2026, 3, 8, 3, 0, 0), "America/Chicago"));
    }

    [Theory]
    [InlineData("UTC", 12, 0)]
    [InlineData("America/Chicago", 7, 0)]
    [InlineData("Asia/Kathmandu", 17, 45)]
    public void Local_ConvertsSameInstantToRequestedDeviceZone(string zone, int hour, int minute)
    {
        var instant = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTime(2026, 9, 21, hour, minute, 0), JournalTime.Local(instant, zone));
        Assert.Equal(instant, JournalTime.Resolve(JournalTime.Local(instant, zone), zone).ToUniversalTime());
    }

    [Fact]
    public void DayStart_ClockChangeAtMidnight_AdvancesToFirstValidMinute()
    {
        var start = JournalTime.DayStart(new(2026, 3, 8), "America/Havana");
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 5, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTime(2026, 3, 8, 1, 0, 0), JournalTime.Local(start, "America/Havana"));
    }

    [Fact]
    public void Zone_UnknownIdentifier_ReturnsActionableError()
    {
        var error = Assert.Throws<JournalException>(() => JournalTime.Zone("IngaCal/Unknown"));
        Assert.Contains("Choose another time zone", error.Message);
    }
}
