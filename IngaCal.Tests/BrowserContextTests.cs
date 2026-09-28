using Bunit;
using IngaCal.Components.Shared;
using IngaCal.Services;
using Microsoft.Extensions.DependencyInjection;

namespace IngaCal.Tests;

public sealed class BrowserContextTests
{
    [Fact]
    public async Task InitializeAndFocusChange_UseDeviceZoneAndNotifySubscribers()
    {
        await using var context = new BunitContext();
        var module = context.JSInterop.SetupModule("./Components/Layout/MainLayout.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        module.Setup<BrowserContext.BrowserState>("initialize", _ => true).SetResult(new("America/Chicago", "dark"));
        await using var browser = new BrowserContext(context.JSInterop.JSRuntime);
        var notifications = new List<string>();
        browser.Changed += () => { notifications.Add(browser.TimeZoneId); return Task.CompletedTask; };

        await browser.InitializeAsync();
        Assert.True(browser.IsReady);
        Assert.Equal("America/Chicago", browser.TimeZoneId);
        Assert.Equal("dark", browser.Theme);
        await browser.InitializeAsync();
        module.VerifyInvoke("initialize", 1);

        await browser.BrowserChanged(new("Asia/Tokyo", "light"));
        Assert.Equal("Asia/Tokyo", browser.TimeZoneId);
        Assert.Equal("light", browser.Theme);
        Assert.Equal(new[] { "America/Chicago", "Asia/Tokyo" }, notifications);
    }

    [Fact]
    public async Task InvalidDeviceZone_BlocksReadyUntilManualSelection()
    {
        await using var context = new BunitContext();
        var module = context.JSInterop.SetupModule("./Components/Layout/MainLayout.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        module.Setup<BrowserContext.BrowserState>("initialize", _ => true).SetResult(new("Invalid/Device", "unknown"));
        module.Setup<BrowserContext.BrowserState>("setZone", "UTC").SetResult(new("UTC", "light"));
        await using var browser = new BrowserContext(context.JSInterop.JSRuntime);

        await browser.InitializeAsync();
        Assert.False(browser.IsReady);
        Assert.Contains("Choose a time zone", browser.Error);
        Assert.Equal("light", browser.Theme);
        await browser.SetZoneAsync("UTC");
        Assert.True(browser.IsReady);
        Assert.Equal("UTC", browser.TimeZoneId);
        Assert.Null(browser.Error);
    }

    [Fact]
    public async Task TimeZonePicker_InvalidThenValidSelection_ShowsErrorThenRecoversAndCanFollowDevice()
    {
        await using var context = new BunitContext();
        var module = context.JSInterop.SetupModule("./Components/Layout/MainLayout.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        module.Setup<BrowserContext.BrowserState>("initialize", _ => true).SetResult(new("America/Chicago", "light"));
        module.Setup<BrowserContext.BrowserState>("setZone", "UTC").SetResult(new("UTC", "light"));
        module.Setup<BrowserContext.BrowserState>("setZone", "").SetResult(new("America/Chicago", "light"));
        await using var browser = new BrowserContext(context.JSInterop.JSRuntime);
        context.Services.AddSingleton(browser);
        await browser.InitializeAsync();
        var picker = context.Render<TimeZonePicker>();

        picker.Find("#manual-zone").Change("Invalid/Zone");
        picker.FindAll("button").Single(x => x.TextContent == "Use time zone").ClickEnabled();
        Assert.Contains("Choose another time zone", picker.Find("[role=alert]").TextContent);
        Assert.Equal("America/Chicago", browser.TimeZoneId);

        picker.Find("#manual-zone").Change("  UTC  ");
        picker.FindAll("button").Single(x => x.TextContent == "Use time zone").ClickEnabled();
        Assert.Empty(picker.FindAll("[role=alert]"));
        Assert.Equal("UTC", browser.TimeZoneId);
        picker.FindAll("button").Single(x => x.TextContent == "Use device").ClickEnabled();
        Assert.Equal("America/Chicago", browser.TimeZoneId);
    }

    [Fact]
    public async Task Dispose_RemovesBrowserSubscriptions()
    {
        await using var context = new BunitContext();
        var module = context.JSInterop.SetupModule("./Components/Layout/MainLayout.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        module.Setup<BrowserContext.BrowserState>("initialize", _ => true).SetResult(new("UTC", "light"));
        var browser = new BrowserContext(context.JSInterop.JSRuntime);
        await browser.InitializeAsync();

        await browser.DisposeAsync();
        module.VerifyInvoke("dispose", 1);
    }
}
