using Microsoft.JSInterop;
namespace IngaCal.Services;

public sealed class BrowserContext(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? module;
    private DotNetObjectReference<BrowserContext>? reference;
    public bool IsReady { get; private set; }
    public string TimeZoneId { get; private set; } = "";
    public string Theme { get; private set; } = "light";
    public string? Error { get; private set; }
    public event Func<Task>? Changed;

    public async Task InitializeAsync()
    {
        if (module is not null) return;
        module = await js.InvokeAsync<IJSObjectReference>("import", "./Components/Layout/MainLayout.razor.js");
        reference = DotNetObjectReference.Create(this);
        var state = await module.InvokeAsync<BrowserState>("initialize", reference);
        await Apply(state);
    }

    [JSInvokable]
    public async Task BrowserChanged(BrowserState state) => await Apply(state);

    private async Task Apply(BrowserState state)
    {
        Theme = state.Theme == "dark" ? "dark" : "light";
        try
        {
            JournalTime.Zone(state.TimeZoneId);
            TimeZoneId = state.TimeZoneId;
            IsReady = true;
            Error = null;
        }
        catch (JournalException)
        {
            IsReady = false;
            Error = "Your device time zone could not be read. Choose a time zone to continue.";
        }
        if (Changed is not null)
            foreach (Func<Task> handler in Changed.GetInvocationList()) await handler();
    }

    public async Task SetZoneAsync(string id)
    {
        JournalTime.Zone(id);
        if (id != "UTC" && TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana)) id = iana;
        if (module is not null)
            await Apply(await module.InvokeAsync<BrowserState>("setZone", id));
    }

    public async Task FollowDeviceAsync()
    {
        if (module is not null)
            await Apply(await module.InvokeAsync<BrowserState>("setZone", ""));
    }

    public DateOnly Today => DateOnly.FromDateTime(JournalTime.Local(DateTimeOffset.UtcNow, TimeZoneId));
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (module is not null)
            {
                await module.InvokeVoidAsync("dispose");
                await module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException) { }
        reference?.Dispose();
    }
    public sealed record BrowserState(string TimeZoneId, string Theme);
}
