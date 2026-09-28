using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
namespace IngaCal.Components.Calendar;
public sealed class EditorInterop(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? module;
    private ElementReference element;
    public async Task OpenAsync(ElementReference target, DotNetObjectReference<ActivityEditor> reference)
    {
        element = target;
        module = await js.InvokeAsync<IJSObjectReference>("import", "./Components/Calendar/ActivityEditor.razor.js");
        await module.InvokeVoidAsync("open", target, reference);
    }
    public async ValueTask DisposeAsync()
    {
        try { if (module is not null) { await module.InvokeVoidAsync("close", element); await module.DisposeAsync(); } }
        catch (JSDisconnectedException) { }
    }
}
