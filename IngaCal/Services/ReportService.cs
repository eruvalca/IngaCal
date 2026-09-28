namespace IngaCal.Services;

public sealed class ReportService(IActivityService activities) : IReportService
{
    public async Task<ReportDto> GetAsync(ReportFilter filter, CancellationToken token = default)
    {
        if (filter.To < filter.From) throw new JournalException("The report end date must be on or after its start date.");
        if (filter.To == DateOnly.MaxValue) throw new JournalException("Choose an end date before December 31, 9999.");
        var start = JournalTime.DayStart(filter.From, filter.TimeZoneId);
        var end = JournalTime.DayStart(filter.To.AddDays(1), filter.TimeZoneId);
        var items = await activities.ListAsync(start, end, token);
        var selected = filter.TagIds.ToHashSet();
        var matching = items.Where(x => filter.UntaggedOnly ? x.Tags.Count == 0 : selected.Count == 0 ||
            (filter.Match == TagMatch.All ? selected.All(id => x.Tags.Any(t => t.Id == id)) : x.Tags.Any(t => selected.Contains(t.Id)))).ToList();
        double total = 0, untagged = 0;
        var totals = new Dictionary<Guid, (string Name, string Color, double Minutes, int Count)>();
        var daily = new SortedDictionary<DateOnly, double>();
        for (var day = filter.From; day <= filter.To; day = day.AddDays(1)) daily[day] = 0;
        foreach (var item in matching)
        {
            token.ThrowIfCancellationRequested();
            var clippedStart = item.Start > start ? item.Start : start;
            var clippedEnd = item.End < end ? item.End : end;
            var minutes = (clippedEnd - clippedStart).TotalMinutes;
            total += minutes;
            if (item.Tags.Count == 0) untagged += minutes;
            foreach (var tag in item.Tags.Where(t => selected.Count == 0 || selected.Contains(t.Id)))
            {
                totals.TryGetValue(tag.Id, out var old);
                totals[tag.Id] = (tag.Name, tag.Color, old.Minutes + minutes, old.Count + 1);
            }
            var first = DateOnly.FromDateTime(JournalTime.Local(clippedStart, filter.TimeZoneId));
            var last = DateOnly.FromDateTime(JournalTime.Local(clippedEnd.AddTicks(-1), filter.TimeZoneId));
            for (var day = first; day <= last; day = day.AddDays(1))
            {
                var a = JournalTime.DayStart(day, filter.TimeZoneId);
                var b = JournalTime.DayStart(day.AddDays(1), filter.TimeZoneId);
                daily[day] += ((b < clippedEnd ? b : clippedEnd) - (a > clippedStart ? a : clippedStart)).TotalMinutes;
            }
        }
        if (untagged > 0) totals[Guid.Empty] = ("Untagged", "#8993a4", untagged, matching.Count(x => x.Tags.Count == 0));
        var tagTotals = totals.Select(x => new TagTotal(x.Key, x.Value.Name, x.Value.Color, x.Value.Minutes, x.Value.Count, total == 0 ? 0 : 100 * x.Value.Minutes / total))
            .OrderByDescending(x => x.Minutes).ThenBy(x => x.Name).ToList();
        var trend = filter.Grouping == ReportGrouping.Daily ? daily.Select(x => new TrendPoint(x.Key, x.Value)).ToList()
            : daily.GroupBy(x => x.Key.AddDays(-(((int)x.Key.DayOfWeek + 6) % 7)))
                .Select(x => new TrendPoint(x.Key, x.Sum(v => v.Value))).ToList();
        return new(total, total / (filter.To.DayNumber - filter.From.DayNumber + 1), untagged, matching.Count, tagTotals, trend);
    }
}
