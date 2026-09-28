using IngaCal.Services;
using Microsoft.AspNetCore.Components;
namespace IngaCal.Components.Shared;

public abstract class JournalPageBase : ComponentBase, IDisposable
{
    [Inject] protected BrowserContext Browser { get; set; } = default!;
    [Inject] protected ILogger<JournalPageBase> Logger { get; set; } = default!;
    protected string? Error;
    protected bool Busy;
    protected readonly CancellationTokenSource Lifetime = new();
    protected override async Task OnInitializedAsync()
    {
        Browser.Changed += BrowserChanged;
        if (Browser.IsReady) await RunAsync(LoadAsync);
    }
    protected abstract Task LoadAsync();
    protected virtual async Task BrowserChanged() => await InvokeAsync(async () =>
    {
        if (Browser.IsReady) await RunAsync(LoadAsync);
        StateHasChanged();
    });
    protected async Task RunAsync(Func<Task> action)
    {
        Error = null;
        try { await action(); }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested) { }
        catch (JournalException e) { Error = e.Message; }
        catch (Exception e) { Logger.LogError(e, "Journal operation failed"); Error = "We couldn't complete that action. Please try again."; }
    }
    public virtual void Dispose()
    {
        Browser.Changed -= BrowserChanged;
        Lifetime.Cancel();
        Lifetime.Dispose();
    }
}
