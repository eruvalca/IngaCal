namespace IngaCal.Data;

public sealed class Activity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public List<ActivityTag> ActivityTags { get; set; } = [];
}

public sealed class Tag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string Color { get; set; } = "#5470c6";
    public bool IsArchived { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

public sealed class ActivityTag
{
    public Guid ActivityId { get; set; }
    public Activity Activity { get; set; } = null!;
    public Guid TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
