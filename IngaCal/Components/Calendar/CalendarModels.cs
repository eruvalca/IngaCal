namespace IngaCal.Components.Calendar;
public record CalendarRange(DateTimeOffset Start, DateTimeOffset End);
public record CalendarSelection(DateTimeOffset Start, DateTimeOffset End);
public sealed record CalendarChange(Guid Id, DateTimeOffset Start, DateTimeOffset End)
{
    public bool Accepted { get; set; }
}
public record CalendarNavigation(DateOnly Date, string View);
public record ActivityDraft(IngaCal.Services.ActivityDto? Activity, DateTimeOffset Start, DateTimeOffset End);
public sealed record NewTagRequest(string Name, string Color)
{
    public IngaCal.Services.TagDto? Created { get; set; }
}
