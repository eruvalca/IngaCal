using System.Text.Json;
using Bunit;
using IngaCal.Components.Reports;
using IngaCal.Services;

namespace IngaCal.Tests;

public sealed class ReportComponentTests
{
    [Fact]
    public void Filters_DefaultToSevenInclusiveDays_AndLastMonthHandlesYearRollover()
    {
        using var context = new BunitContext();
        ReportFilter? submitted = null;
        var filters = context.Render<ReportFilters>(parameters => parameters
            .Add(x => x.Today, new DateOnly(2026, 1, 3)).Add(x => x.TimeZoneId, "America/Chicago")
            .Add(x => x.Changed, value => submitted = value));

        filters.FindAll("button").Single(x => x.TextContent == "Update report").ClickEnabled();
        var initial = Assert.IsType<ReportFilter>(submitted);
        Assert.Equal(new DateOnly(2025, 12, 28), initial.From);
        Assert.Equal(new DateOnly(2026, 1, 3), initial.To);
        Assert.Equal("America/Chicago", initial.TimeZoneId);
        Assert.Equal(ReportGrouping.Daily, initial.Grouping);
        Assert.Empty(initial.TagIds);

        filters.FindAll("button").Single(x => x.TextContent == "Last month").ClickEnabled();
        Assert.Equal(new DateOnly(2025, 12, 1), submitted!.From);
        Assert.Equal(new DateOnly(2025, 12, 31), submitted.To);
    }

    [Fact]
    public void Filters_CustomRangeTagsAndUntaggedMode_ProduceExplicitFilterAndClearSelection()
    {
        using var context = new BunitContext();
        var tag = new TagDto(Guid.NewGuid(), "Learning", "#123456", true, Guid.NewGuid());
        ReportFilter? submitted = null;
        var filters = context.Render<ReportFilters>(parameters => parameters
            .Add(x => x.Today, new DateOnly(2026, 9, 28)).Add(x => x.Tags, [tag])
            .Add(x => x.Changed, value => submitted = value));
        filters.Find("#report-from").Change("2026-09-01");
        filters.Find("#report-to").Change("2026-09-20");
        filters.Find("#report-group").Change("Weekly");
        filters.Find("#tag-match").Change("All");
        var tagButton = filters.FindAll("button").Single(x => x.TextContent.Contains("Learning"));
        tagButton.ClickEnabled();
        Assert.Equal("true", filters.FindAll("button").Single(x => x.TextContent.Contains("Learning")).GetAttribute("aria-pressed"));
        filters.FindAll("button").Single(x => x.TextContent == "Update report").ClickEnabled();

        Assert.Equal(new DateOnly(2026, 9, 1), submitted!.From);
        Assert.Equal(new DateOnly(2026, 9, 20), submitted.To);
        Assert.Equal(ReportGrouping.Weekly, submitted.Grouping);
        Assert.Equal(TagMatch.All, submitted.Match);
        Assert.Equal(new[] { tag.Id }, submitted.TagIds);
        Assert.False(submitted.UntaggedOnly);
        filters.Find("input[type=checkbox]").Change(true);
        Assert.True(filters.Find("#tag-match").HasAttribute("disabled"));
        filters.FindAll("button").Single(x => x.TextContent == "Update report").ClickEnabled();
        Assert.True(submitted.UntaggedOnly);

        filters.FindAll("button").Single(x => x.TextContent == "Clear tags").ClickEnabled();
        filters.FindAll("button").Single(x => x.TextContent == "Update report").ClickEnabled();
        Assert.Empty(submitted.TagIds);
        Assert.False(submitted.UntaggedOnly);
    }

    [Fact]
    public void Tables_SortTagTotalsAndExposeExactTrendValues()
    {
        using var culture = new InvariantCultureScope();
        using var context = new BunitContext();
        var tables = context.Render<ReportTables>(parameters => parameters.Add(x => x.Data, ExampleReport()));
        Assert.Equal(new[] { "Work", "Learning" }, tables.FindAll("table:first-of-type tbody tr th").Take(2).Select(x => x.TextContent.Trim()));
        tables.FindAll("button").Single(x => x.TextContent.Trim() == "Tag").ClickEnabled();
        var rows = tables.FindAll("table")[0].QuerySelectorAll("tbody tr");
        Assert.Equal(new[] { "Learning", "Work" }, rows.Select(x => x.QuerySelector("th")!.TextContent.Trim()));
        Assert.Equal("30m", rows[0].QuerySelectorAll("td")[0].TextContent);
        Assert.Equal("25%", rows[0].QuerySelectorAll("td")[2].TextContent);
        Assert.Equal("ascending", tables.FindAll("table")[0].QuerySelector("thead th")!.GetAttribute("aria-sort"));

        var trendRows = tables.FindAll("table")[1].QuerySelectorAll("tbody tr");
        Assert.Equal(new[] { "1h 30m", "0m", "30m" }, trendRows.Select(x => x.QuerySelectorAll("td")[0].TextContent));
        Assert.Equal(new[] { "1.5", "0", "0.5" }, trendRows.Select(x => x.QuerySelectorAll("td")[1].TextContent));
    }

    [Fact]
    public async Task Charts_UseSameReportValuesAsTables_ConvertingMinutesToHours()
    {
        using var culture = new InvariantCultureScope();
        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.SetupModule(ReportChartLabelsInterop.ModulePath).Mode = JSRuntimeMode.Loose;
        var report = ExampleReport();
        var charts = context.Render<ReportCharts>(parameters => parameters.Add(x => x.Data, report));
        var calls = context.JSInterop.Invocations.Where(x => x.Identifier.EndsWith(".initialize", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, calls.Length);
        var serialization = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var barData = JsonSerializer.SerializeToElement(Assert.Single(calls, x => x.Identifier.Contains(".bar.")).Arguments[2], serialization);
        var lineData = JsonSerializer.SerializeToElement(Assert.Single(calls, x => x.Identifier.Contains(".line.")).Arguments[2], serialization);
        var pieData = JsonSerializer.SerializeToElement(Assert.Single(calls, x => x.Identifier.Contains(".pie.")).Arguments[2], serialization);
        Assert.Equal(new[] { "Learning", "Work" }, barData.GetProperty("labels").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(new double[] { .5, 1.5 }, barData.GetProperty("datasets")[0].GetProperty("data").EnumerateArray().Select(x => x.GetDouble()));
        Assert.Equal(new double[] { 1.5, 0, .5 }, lineData.GetProperty("datasets")[0].GetProperty("data").EnumerateArray().Select(x => x.GetDouble()));
        Assert.Equal(new[] { "Sep 21", "Sep 22", "Sep 23" }, lineData.GetProperty("labels").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(new[] { "Learning (25%)", "Work (75%)" }, pieData.GetProperty("labels").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(new double[] { .5, 1.5 }, pieData.GetProperty("datasets")[0].GetProperty("data").EnumerateArray().Select(x => x.GetDouble()));
        Assert.Equal(new[] { "#abcdef", "#123456" }, pieData.GetProperty("datasets")[0].GetProperty("backgroundColor").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(new[] { "30m", "1h 30m" }, charts.FindAll("[aria-label='Pie chart legend'] li .tabular").Select(x => x.TextContent));
        Assert.Equal(new[] { "25%", "75%" }, charts.FindAll(".pie-percentage").Select(x => x.TextContent));
        Assert.Equal(3, charts.FindAll("[role=img][aria-label]").Count);
    }

    [Fact]
    public async Task PieChart_MultiTagAndUntaggedTotals_UpdateWithFiltersAndTheme()
    {
        using var culture = new InvariantCultureScope();
        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var labelModule = context.JSInterop.SetupModule(ReportChartLabelsInterop.ModulePath);
        labelModule.Mode = JSRuntimeMode.Loose;
        var report = new ReportDto(75, 75, 30, 2,
            [new(Guid.NewGuid(), "Work", "#123456", 45, 1, 60), new(Guid.NewGuid(), "Focus", "#abcdef", 45, 1, 60), new(Guid.Empty, "Untagged", "#8993a4", 30, 1, 40)],
            [new(new(2026, 9, 28), 75)]);
        var charts = context.Render<ReportCharts>(parameters => parameters.Add(x => x.Data, report));
        var serialization = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var initialization = Assert.Single(context.JSInterop.Invocations, x => x.Identifier.EndsWith(".pie.initialize", StringComparison.Ordinal));
        Assert.Equal("pie", initialization.Arguments[1]);
        var initialData = JsonSerializer.SerializeToElement(initialization.Arguments[2], serialization);
        Assert.Equal(new[] { "Work (37.5%)", "Focus (37.5%)", "Untagged (25%)" }, initialData.GetProperty("labels").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(new[] { "37.5%", "37.5%", "25%" }, charts.FindAll(".pie-percentage").Select(x => x.TextContent));
        Assert.Equal(new double[] { .75, .75, .5 }, initialData.GetProperty("datasets")[0].GetProperty("data").EnumerateArray().Select(x => x.GetDouble()));
        Assert.Equal("#ffffff", initialData.GetProperty("datasets")[0].GetProperty("borderColor")[0].GetString());
        Assert.Contains("Activities with multiple tags count in each assigned tag.", charts.Markup);
        var initialLabels = labelModule.VerifyInvoke("updateLabels");
        Assert.Equal(new[] { new ChartValueLabel("45m", "60%"), new ChartValueLabel("45m", "60%"), new ChartValueLabel("30m", "40%") }, Assert.IsType<ChartValueLabel[]>(initialLabels.Arguments[2]));
        Assert.Equal(new[] { new ChartValueLabel("45m", "37.5%"), new ChartValueLabel("45m", "37.5%"), new ChartValueLabel("30m", "25%") }, Assert.IsType<ChartValueLabel[]>(initialLabels.Arguments[3]));

        var untaggedReport = new ReportDto(30, 30, 30, 1, [report.Tags[2] with { Percentage = 100 }], [new(new(2026, 9, 28), 30)]);
        charts.Render(parameters => parameters.Add(x => x.Data, untaggedReport).Add(x => x.Theme, "dark"));
        var update = Assert.Single(context.JSInterop.Invocations, x => x.Identifier.EndsWith(".pie.update", StringComparison.Ordinal));
        var updatedData = JsonSerializer.SerializeToElement(update.Arguments[2], serialization);
        Assert.Equal(new[] { "Untagged (100%)" }, updatedData.GetProperty("labels").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("100%", Assert.Single(charts.FindAll(".pie-percentage")).TextContent);
        Assert.Equal(new double[] { .5 }, updatedData.GetProperty("datasets")[0].GetProperty("data").EnumerateArray().Select(x => x.GetDouble()));
        Assert.Equal("#8993a4", updatedData.GetProperty("datasets")[0].GetProperty("backgroundColor")[0].GetString());
        Assert.Equal("#202c25", updatedData.GetProperty("datasets")[0].GetProperty("borderColor")[0].GetString());
        var legendItem = Assert.Single(charts.FindAll("[aria-label='Pie chart legend'] li"));
        Assert.Contains("Untagged", legendItem.TextContent);
        Assert.Contains("30m", legendItem.TextContent);
        Assert.Single(context.JSInterop.Invocations, x => x.Identifier.EndsWith(".pie.initialize", StringComparison.Ordinal));
        var updatedLabels = labelModule.Invocations.Last(x => x.Identifier == "updateLabels");
        Assert.Equal(new[] { new ChartValueLabel("30m", "100%") }, Assert.IsType<ChartValueLabel[]>(updatedLabels.Arguments[2]));
        Assert.Equal(new[] { new ChartValueLabel("30m", "100%") }, Assert.IsType<ChartValueLabel[]>(updatedLabels.Arguments[3]));
        Assert.Equal("dark", updatedLabels.Arguments[4]);
        await context.DisposeComponentsAsync();
        labelModule.VerifyInvoke("disposeLabels");
    }

    [Fact]
    public async Task PieChart_ZeroTotal_ShowsZeroPercentWithoutInvalidValues()
    {
        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.SetupModule(ReportChartLabelsInterop.ModulePath).Mode = JSRuntimeMode.Loose;
        var report = new ReportDto(0, 0, 0, 0, [new(Guid.Empty, "Untagged", "#8993a4", 0, 0, 0)], []);
        var charts = context.Render<ReportCharts>(parameters => parameters.Add(x => x.Data, report));
        Assert.Equal("0%", charts.Find(".pie-percentage").TextContent);
        var call = Assert.Single(context.JSInterop.Invocations, x => x.Identifier.EndsWith(".pie.initialize", StringComparison.Ordinal));
        var data = JsonSerializer.SerializeToElement(call.Arguments[2], new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("Untagged (0%)", data.GetProperty("labels")[0].GetString());
        Assert.Equal(0, data.GetProperty("datasets")[0].GetProperty("data")[0].GetDouble());
    }

    private static ReportDto ExampleReport() => new(120, 40, 0, 3,
        [new(Guid.NewGuid(), "Learning", "#abcdef", 30, 1, 25), new(Guid.NewGuid(), "Work", "#123456", 90, 2, 75)],
        [new(new(2026, 9, 21), 90), new(new(2026, 9, 22), 0), new(new(2026, 9, 23), 30)]);
}
