using System.Text.Json;
using Bunit;
using IngaCal.Components.Calendar;
using IngaCal.Components.Pages;
using IngaCal.Components.Reports;
using IngaCal.Services;
using Microsoft.Extensions.DependencyInjection;
using ReportsPage = IngaCal.Components.Pages.Reports;

namespace IngaCal.Tests;

public sealed class PageIntegrationTests
{
    [Fact]
    public async Task Home_InitializesCalendarWithActualViewAndDeviceZone_AndUpdatesViewFromToolbar()
    {
        await using var journal = await SqliteJournal.CreateAsync();
        await using var context = new BunitContext();
        var calendarModule = context.JSInterop.SetupModule("./Components/Calendar/CalendarSurface.razor.js");
        calendarModule.Mode = JSRuntimeMode.Loose;
        var browser = new BrowserContext(context.JSInterop.JSRuntime);
        await browser.BrowserChanged(new("Asia/Kathmandu", "dark"));
        context.Services.AddLogging();
        context.Services.AddSingleton(browser);
        context.Services.AddSingleton<IActivityService>(journal.Activities());
        context.Services.AddSingleton<ITagService>(journal.Tags());

        var page = context.Render<Home>();
        page.WaitForAssertion(() => Assert.Single(calendarModule.Invocations, x => x.Identifier == "initialize"));
        var initialization = JsonSerializer.SerializeToElement(calendarModule.VerifyInvoke("initialize").Arguments[2]);
        Assert.Equal("timeGridDay", initialization.GetProperty("view").GetString());
        Assert.Equal("Asia/Kathmandu", initialization.GetProperty("timeZone").GetString());
        Assert.Equal(browser.Today.ToString("yyyy-MM-dd"), initialization.GetProperty("date").GetString());
        Assert.Equal("timeGridDay", page.FindComponent<CalendarToolbar>().Instance.View);
        Assert.Empty(page.FindAll("[role=alert]"));

        page.FindAll("button").Single(x => x.TextContent == "Week").ClickEnabled();
        var update = JsonSerializer.SerializeToElement(calendarModule.Invocations.Last(x => x.Identifier == "update").Arguments[1]);
        Assert.Equal("timeGridWeek", update.GetProperty("view").GetString());
        Assert.Equal("Asia/Kathmandu", update.GetProperty("timeZone").GetString());
        Assert.Equal("timeGridWeek", page.FindComponent<CalendarToolbar>().Instance.View);
    }

    [Fact]
    public async Task Reports_PassesActualDeviceZoneToFiltersAndDarkThemeToCharts()
    {
        await using var journal = await SqliteJournal.CreateAsync();
        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.SetupModule(ReportChartLabelsInterop.ModulePath).Mode = JSRuntimeMode.Loose;
        var browser = new BrowserContext(context.JSInterop.JSRuntime);
        await browser.BrowserChanged(new("Asia/Kathmandu", "dark"));
        var start = JournalTime.Resolve(browser.Today.ToDateTime(new TimeOnly(9, 0)), browser.TimeZoneId);
        await journal.Activities().SaveAsync(SqliteJournal.Edit(start, start.AddHours(1)));
        context.Services.AddLogging();
        context.Services.AddSingleton(browser);
        context.Services.AddSingleton<ITagService>(journal.Tags());
        context.Services.AddSingleton<IReportService>(journal.Reports());

        var page = context.Render<ReportsPage>();
        page.WaitForAssertion(() => Assert.Single(page.FindComponents<ReportCharts>()));
        Assert.Equal("Asia/Kathmandu", page.FindComponent<ReportFilters>().Instance.TimeZoneId);
        Assert.Equal("dark", page.FindComponent<ReportCharts>().Instance.Theme);
        Assert.Contains("1h 00m", page.FindComponent<ReportSummary>().Markup);
        Assert.Empty(page.FindAll("[role=alert]"));

        var barInitialization = Assert.Single(context.JSInterop.Invocations, x => x.Identifier.EndsWith(".bar.initialize", StringComparison.Ordinal));
        var options = JsonSerializer.SerializeToElement(barInitialization.Arguments[3], new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("#d9e5df", options.GetProperty("scales").GetProperty("x").GetProperty("ticks").GetProperty("color").GetString());
        page.FindAll("button").Single(x => x.TextContent == "Update report").ClickEnabled();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[role=alert]")));
        Assert.Equal(60, page.FindComponent<ReportSummary>().Instance.Data.TotalMinutes);
    }
}
