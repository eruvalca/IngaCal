using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace IngaCal.Components.Reports;

public sealed record ChartValueLabel(string Duration, string Percentage);

public sealed class ReportChartLabelsInterop(IJSRuntime js) : IAsyncDisposable
{
    public const string ModulePath = "./Components/Reports/ReportCharts.razor.js";
    private const string UpdateMethod = "updateLabels";
    private const string DisposeMethod = "disposeLabels";
    private IJSObjectReference? module;
    private ElementReference barRegion, pieRegion;

    public async Task UpdateAsync(ElementReference bars, ElementReference pie, ChartValueLabel[] barLabels, ChartValueLabel[] pieLabels, string theme)
    {
        barRegion = bars;
        pieRegion = pie;
        module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        await module.InvokeVoidAsync(UpdateMethod, bars, pie, barLabels, pieLabels, theme);
    }

    public async ValueTask DisposeAsync()
    {
        if (module is null) return;
        try
        {
            await module.InvokeVoidAsync(DisposeMethod, barRegion, pieRegion);
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
    }
}
