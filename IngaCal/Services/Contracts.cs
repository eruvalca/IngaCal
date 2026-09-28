namespace IngaCal.Services;

public sealed class JournalException(string message) : Exception(message);
public record TagDto(Guid Id, string Name, string Color, bool IsArchived, Guid Version);
public record ActivityDto(Guid Id, string Title, string Description, DateTimeOffset Start, DateTimeOffset End, Guid Version, IReadOnlyList<TagDto> Tags)
{
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? Description.Split('\n')[0] : Title;
    public double Minutes => (End - Start).TotalMinutes;
}
public record ActivityEdit(Guid? Id, string Title, string Description, DateTimeOffset Start, DateTimeOffset End, IReadOnlyList<Guid> TagIds, Guid Version = default);
public record TagEdit(Guid? Id, string Name, string Color, bool IsArchived = false, Guid Version = default);
public enum TagMatch { Any, All }
public enum ReportGrouping { Daily, Weekly }
public record ReportFilter(DateOnly From, DateOnly To, string TimeZoneId, IReadOnlyList<Guid> TagIds, TagMatch Match = TagMatch.Any, bool UntaggedOnly = false, ReportGrouping Grouping = ReportGrouping.Daily);
public record TagTotal(Guid Id, string Name, string Color, double Minutes, int ActivityCount, double Percentage);
public record TrendPoint(DateOnly Date, double Minutes);
public record ReportDto(double TotalMinutes, double AverageMinutes, double UntaggedMinutes, int ActivityCount, IReadOnlyList<TagTotal> Tags, IReadOnlyList<TrendPoint> Trend);

public interface IActivityService
{
    Task<IReadOnlyList<ActivityDto>> ListAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken token = default);
    Task<ActivityDto> SaveAsync(ActivityEdit edit, CancellationToken token = default);
    Task DeleteAsync(Guid id, Guid version, CancellationToken token = default);
}
public interface ITagService
{
    Task<IReadOnlyList<TagDto>> ListAsync(CancellationToken token = default);
    Task<TagDto> SaveAsync(TagEdit edit, CancellationToken token = default);
}
public interface IReportService
{
    Task<ReportDto> GetAsync(ReportFilter filter, CancellationToken token = default);
}

public static class DurationText
{
    public static string Format(double minutes)
    {
        var rounded = (long)Math.Round(minutes);
        return rounded >= 60 ? $"{rounded / 60:N0}h {rounded % 60:00}m" : $"{rounded}m";
    }
}
