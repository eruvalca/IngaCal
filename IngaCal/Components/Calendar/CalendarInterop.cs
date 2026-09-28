using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
namespace IngaCal.Components.Calendar;
public sealed class CalendarInterop(IJSRuntime js) : IAsyncDisposable
{
    private const string Path = "./Components/Calendar/CalendarSurface.razor.js";
    private IJSObjectReference? module;
    private ElementReference element;
    public async Task InitializeAsync(ElementReference target, DotNetObjectReference<CalendarSurface> reference, object state)
    {
        element = target;
        module = await js.InvokeAsync<IJSObjectReference>("import", Path);
        await module.InvokeVoidAsync("initialize", target, reference, state);
    }
    public async Task UpdateAsync(ElementReference target, object state)
    {
        if (module is not null) await module.InvokeVoidAsync("update", target, state);
    }
    public async ValueTask DisposeAsync()
    {
        try { if (module is not null) { await module.InvokeVoidAsync("dispose", element); await module.DisposeAsync(); } }
        catch (JSDisconnectedException) { }
    }
}
