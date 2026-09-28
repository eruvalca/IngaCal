namespace IngaCal.Services;

public static class JournalTime
{
    public static TimeZoneInfo Zone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { throw new JournalException("This time zone is unavailable. Choose another time zone."); }
        catch (InvalidTimeZoneException) { throw new JournalException("This time zone is unavailable. Choose another time zone."); }
    }

    public static DateTime Local(DateTimeOffset instant, string zone) => TimeZoneInfo.ConvertTime(instant, Zone(zone)).DateTime;
    public static TimeSpan[] Offsets(DateTime local, string zone)
    {
        var tz = Zone(zone);
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return tz.IsAmbiguousTime(local) ? tz.GetAmbiguousTimeOffsets(local).OrderDescending().ToArray() : [];
    }

    public static DateTimeOffset Resolve(DateTime local, string zone, bool laterOccurrence = false)
    {
        var tz = Zone(zone);
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(local)) throw new JournalException("That local time does not exist because the clocks move forward. Choose a time after the clock change.");
        var offsets = Offsets(local, zone);
        var offset = offsets.Length > 0 ? offsets[laterOccurrence ? ^1 : 0] : tz.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    public static DateTimeOffset DayStart(DateOnly date, string zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        var tz = Zone(zone);
        // Some regions transition at midnight or skip a date entirely.
        while (tz.IsInvalidTime(local)) local = local.AddMinutes(1);
        return Resolve(local, zone).ToUniversalTime();
    }
}
