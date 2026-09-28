using System.ComponentModel.DataAnnotations;
using IngaCal.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
namespace IngaCal.Components.Calendar;

public partial class ActivityEditor : IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Parameter, EditorRequired] public ActivityDraft Draft { get; set; } = default!;
    [Parameter] public IReadOnlyList<TagDto> Tags { get; set; } = [];
    [Parameter] public string? Error { get; set; }
    [Parameter] public bool Busy { get; set; }
    [Parameter] public EventCallback<ActivityEdit> Save { get; set; }
    [Parameter] public EventCallback Cancel { get; set; }
    [Parameter] public EventCallback Delete { get; set; }
    [Parameter] public EventCallback<NewTagRequest> CreateTag { get; set; }
    private ElementReference dialog;
    private EditorInterop? interop;
    private DotNetObjectReference<ActivityEditor>? reference;
    private FormModel model = new();
    private IReadOnlyList<Guid> selectedIds = [];
    private string? localError;
    private bool confirmDelete;
    protected override void OnInitialized()
    {
        model = new() { Id = Draft.Activity?.Id, Version = Draft.Activity?.Version ?? Guid.Empty,
            Title = Draft.Activity?.Title ?? "", Description = Draft.Activity?.Description ?? "",
            Start = JournalTime.Local(Draft.Start, Browser.TimeZoneId), End = JournalTime.Local(Draft.End, Browser.TimeZoneId) };
        model.StartLater = IsLater(model.Start, Draft.Start);
        model.EndLater = IsLater(model.End, Draft.End);
        selectedIds = Draft.Activity?.Tags.Select(x => x.Id).ToArray() ?? [];
    }
    private bool IsLater(DateTime local, DateTimeOffset instant) =>
        JournalTime.Offsets(local, Browser.TimeZoneId).Length > 0 && JournalTime.Resolve(local, Browser.TimeZoneId, true).UtcDateTime == instant.UtcDateTime;
    private string Preview
    {
        get
        {
            try
            {
                var duration = (JournalTime.Resolve(model.End, Browser.TimeZoneId, model.EndLater) - JournalTime.Resolve(model.Start, Browser.TimeZoneId, model.StartLater)).TotalMinutes;
                return duration > 0 ? DurationText.Format(duration) : "End must follow start";
            }
            catch (JournalException) { return "Check the clock-change time"; }
        }
    }
    private async Task Submit()
    {
        localError = null;
        try { await Save.InvokeAsync(new(model.Id, model.Title, model.Description,
            JournalTime.Resolve(model.Start, Browser.TimeZoneId, model.StartLater),
            JournalTime.Resolve(model.End, Browser.TimeZoneId, model.EndLater), selectedIds, model.Version)); }
        catch (JournalException e) { localError = e.Message; }
    }
    private void Duplicate()
    {
        model.Id = null; model.Version = Guid.Empty;
        model.Start = model.Start.AddDays(1); model.End = model.End.AddDays(1);
        model.StartLater = model.EndLater = false; confirmDelete = false;
        var hadArchived = Tags.Any(x => x.IsArchived && selectedIds.Contains(x.Id));
        selectedIds = selectedIds.Where(id => Tags.Any(x => x.Id == id && !x.IsArchived)).ToArray();
        localError = hadArchived ? "Copy ready for tomorrow. Archived tags were left off; restore them on the Tags page to reuse them." : null;
    }
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender) { interop = new(JS); reference = DotNetObjectReference.Create(this); await interop.OpenAsync(dialog, reference); }
    }
    [JSInvokable] public Task OnDismiss() => Busy ? Task.CompletedTask : InvokeAsync(() => Cancel.InvokeAsync());
    public async ValueTask DisposeAsync()
    {
        if (interop is not null) await interop.DisposeAsync();
        reference?.Dispose();
    }
    public sealed class FormModel : IValidatableObject
    {
        public Guid? Id { get; set; }
        public Guid Version { get; set; }
        [StringLength(200)] public string Title { get; set; } = "";
        [StringLength(4000)] public string Description { get; set; } = "";
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public bool StartLater { get; set; }
        public bool EndLater { get; set; }
        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(Description))
                yield return new("Add a title or a description.", [nameof(Title), nameof(Description)]);
        }
    }
}
