using Bunit;
using IngaCal.Components.Calendar;
using IngaCal.Services;
using Microsoft.Extensions.DependencyInjection;

namespace IngaCal.Tests;

public sealed class CalendarComponentTests
{
    [Theory]
    [InlineData("timeGridDay", 2, 1)]
    [InlineData("timeGridWeek", 2, 7)]
    [InlineData("dayGridMonth", 2, 28)]
    public void Toolbar_NextPeriod_UsesCurrentViewAndHandlesMonthRollover(string view, int month, int day)
    {
        using var context = new BunitContext();
        CalendarNavigation? requested = null;
        var toolbar = context.Render<CalendarToolbar>(parameters => parameters
            .Add(x => x.Date, new DateOnly(2026, 1, 31)).Add(x => x.View, view)
            .Add(x => x.Navigate, value => requested = value));
        toolbar.Find("[aria-label='Next period']").ClickEnabled();
        Assert.Equal(new CalendarNavigation(new(2026, month, day), view), requested);
    }

    [Fact]
    public void TagPicker_SearchAndSelection_KeepSelectedArchivedTagsButHideUnselectedArchivedTags()
    {
        using var context = new BunitContext();
        var active = new TagDto(Guid.NewGuid(), "Learning", "#123456", false, Guid.NewGuid());
        var selectedArchive = new TagDto(Guid.NewGuid(), "Old reading", "#abcdef", true, Guid.NewGuid());
        var hiddenArchive = new TagDto(Guid.NewGuid(), "Retired", "#654321", true, Guid.NewGuid());
        IReadOnlyList<Guid>? selected = null;
        var picker = context.Render<TagPicker>(parameters => parameters
            .Add(x => x.Tags, [active, selectedArchive, hiddenArchive])
            .Add(x => x.SelectedIds, [selectedArchive.Id])
            .Add(x => x.SelectedIdsChanged, value => selected = value));

        Assert.Equal(2, picker.FindAll(".tag-choice").Count);
        Assert.DoesNotContain("Retired", picker.Markup);
        var old = picker.FindAll(".tag-choice").Single(x => x.TextContent.Contains("Old reading"));
        Assert.Equal("true", old.GetAttribute("aria-pressed"));
        old.ClickEnabled();
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<Guid>>(selected));

        picker.Find("#tag-search").Input("LEARN");
        var visible = Assert.Single(picker.FindAll(".tag-choice"));
        Assert.Contains("Learning", visible.TextContent);
        visible.ClickEnabled();
        Assert.Equal(new[] { selectedArchive.Id, active.Id }, selected);
    }

    [Fact]
    public void TagPicker_InlineCreation_SelectsNewTagAndClearsDraftOnlyOnSuccess()
    {
        using var context = new BunitContext();
        var created = new TagDto(Guid.NewGuid(), "Reading", "#abcdef", false, Guid.NewGuid());
        var createSucceeds = false;
        IReadOnlyList<Guid>? selected = null;
        NewTagRequest? captured = null;
        var picker = context.Render<TagPicker>(parameters => parameters
            .Add(x => x.CreateTag, request => { captured = request; if (createSucceeds) request.Created = created; })
            .Add(x => x.SelectedIdsChanged, value => selected = value));
        var name = picker.Find("[aria-label='New tag name']");
        Assert.True(picker.FindAll("button").Single(x => x.TextContent == "Add tag").HasAttribute("disabled"));
        name.Input("Reading");
        picker.Find("[aria-label='New tag color']").Change("#abcdef");
        picker.FindAll("button").Single(x => x.TextContent == "Add tag").ClickEnabled();

        Assert.Null(selected);
        Assert.Equal("Reading", picker.Find("[aria-label='New tag name']").GetAttribute("value"));
        createSucceeds = true;
        picker.FindAll("button").Single(x => x.TextContent == "Add tag").ClickEnabled();

        Assert.Equal("Reading", captured?.Name);
        Assert.Equal("#abcdef", captured?.Color);
        Assert.Equal(new[] { created.Id }, selected);
        Assert.Equal("", picker.Find("[aria-label='New tag name']").GetAttribute("value"));
    }

    [Fact]
    public async Task Editor_ManualExactMinuteEntry_ValidatesTextAndSendsUtcCrossMidnightTimes()
    {
        await using var context = await EditorContextAsync();
        ActivityEdit? submitted = null;
        var editor = context.Render<ActivityEditor>(parameters => parameters
            .Add(x => x.Draft, new ActivityDraft(null, SqlServerJournal.Morning, SqlServerJournal.Morning.AddHours(1)))
            .Add(x => x.Save, value => submitted = value));

        editor.Find("form").SubmitWithEnabledButton();
        Assert.Null(submitted);
        Assert.Contains("Add a title or a description", editor.Markup);
        editor.Find("#activity-description").Change("Reading before bed");
        editor.Find("#activity-start").Change("2026-09-21T23:53");
        editor.Find("#activity-end").Change("2026-09-22T00:15");
        Assert.Contains("22m", editor.Find(".duration-preview").TextContent);
        editor.Find("form").SubmitWithEnabledButton();

        var saved = Assert.IsType<ActivityEdit>(submitted);
        Assert.Null(saved.Id);
        Assert.Equal("", saved.Title);
        Assert.Equal("Reading before bed", saved.Description);
        Assert.Equal(new DateTimeOffset(2026, 9, 22, 4, 53, 0, TimeSpan.Zero), saved.Start.ToUniversalTime());
        Assert.Equal(new DateTimeOffset(2026, 9, 22, 5, 15, 0, TimeSpan.Zero), saved.End.ToUniversalTime());
    }

    [Fact]
    public async Task Editor_CopyCreatesEditableTomorrowDraft_WithoutUpdatingOriginalOrReusingArchivedTags()
    {
        await using var context = await EditorContextAsync();
        var active = new TagDto(Guid.NewGuid(), "Active", "#123456", false, Guid.NewGuid());
        var archived = new TagDto(Guid.NewGuid(), "Archived", "#abcdef", true, Guid.NewGuid());
        var original = new ActivityDto(Guid.NewGuid(), "Original", "Notes", SqlServerJournal.Morning, SqlServerJournal.Morning.AddMinutes(37), Guid.NewGuid(), [active, archived]);
        ActivityEdit? submitted = null;
        var editor = context.Render<ActivityEditor>(parameters => parameters
            .Add(x => x.Draft, new ActivityDraft(original, original.Start, original.End))
            .Add(x => x.Tags, [active, archived]).Add(x => x.Save, value => submitted = value));

        editor.FindAll("button").Single(x => x.TextContent.Trim() == "Copy").ClickEnabled();
        Assert.Null(submitted);
        Assert.Equal("Add an activity", editor.Find("h2").TextContent);
        Assert.Contains("Archived tags were left off", editor.Find("[role=alert]").TextContent);
        editor.Find("#activity-title").Change("Copy edited");
        editor.Find("form").SubmitWithEnabledButton();

        var saved = Assert.IsType<ActivityEdit>(submitted);
        Assert.Null(saved.Id);
        Assert.Equal(Guid.Empty, saved.Version);
        Assert.Equal("Copy edited", saved.Title);
        Assert.Equal(original.Start.AddDays(1), saved.Start);
        Assert.Equal(original.End.AddDays(1), saved.End);
        Assert.Equal(new[] { active.Id }, saved.TagIds);
        Assert.Equal("Original", original.Title);
    }

    [Fact]
    public async Task Editor_DeleteRequiresConfirmation_AndKeepItCancels()
    {
        await using var context = await EditorContextAsync();
        var original = new ActivityDto(Guid.NewGuid(), "Original", "", SqlServerJournal.Morning, SqlServerJournal.Morning.AddHours(1), Guid.NewGuid(), []);
        var deleteCount = 0;
        var editor = context.Render<ActivityEditor>(parameters => parameters
            .Add(x => x.Draft, new ActivityDraft(original, original.Start, original.End))
            .Add(x => x.Delete, () => deleteCount++));

        editor.FindAll("button").Single(x => x.TextContent == "Delete").ClickEnabled();
        Assert.Equal(0, deleteCount);
        Assert.Contains("This cannot be undone", editor.Markup);
        editor.FindAll("button").Single(x => x.TextContent == "Keep it").ClickEnabled();
        Assert.DoesNotContain("This cannot be undone", editor.Markup);
        editor.FindAll("button").Single(x => x.TextContent == "Delete").ClickEnabled();
        editor.FindAll("button").Single(x => x.TextContent == "Delete activity").ClickEnabled();
        Assert.Equal(1, deleteCount);
    }

    [Fact]
    public async Task Editor_ActionsAreEnabledWhenIdle_DisabledWhileBusy_AndEnabledAfterCompletion()
    {
        await using var context = await EditorContextAsync();
        var original = new ActivityDto(Guid.NewGuid(), "Original", "", SqlServerJournal.Morning, SqlServerJournal.Morning.AddHours(1), Guid.NewGuid(), []);
        var editor = context.Render<ActivityEditor>(parameters => parameters
            .Add(x => x.Draft, new ActivityDraft(original, original.Start, original.End))
            .Add(x => x.Busy, false));

        var actions = editor.FindAll(".editor-heading button, .editor-actions button");
        Assert.Equal(5, actions.Count);
        Assert.All(actions, action => Assert.False(action.HasAttribute("disabled"), action.OuterHtml));
        editor.FindAll("button").Single(x => x.TextContent == "Delete").ClickEnabled();
        Assert.False(editor.FindAll("button").Single(x => x.TextContent == "Delete activity").HasAttribute("disabled"));

        editor.Render(parameters => parameters.Add(x => x.Busy, true));
        actions = editor.FindAll(".editor-heading button, .editor-actions button");
        Assert.Equal(5, actions.Count);
        Assert.All(actions, action => Assert.True(action.HasAttribute("disabled"), action.OuterHtml));
        Assert.True(editor.FindAll("button").Single(x => x.TextContent == "Delete activity").HasAttribute("disabled"));
        Assert.Equal("Saving…", editor.Find("button[type=submit]").TextContent);

        editor.Render(parameters => parameters.Add(x => x.Busy, false));
        Assert.All(editor.FindAll(".editor-heading button, .editor-actions button"), action => Assert.False(action.HasAttribute("disabled"), action.OuterHtml));
        Assert.False(editor.FindAll("button").Single(x => x.TextContent == "Delete activity").HasAttribute("disabled"));
        Assert.Equal("Save activity", editor.Find("button[type=submit]").TextContent);
    }

    private static async Task<BunitContext> EditorContextAsync()
    {
        var context = new BunitContext();
        context.JSInterop.SetupModule("./Components/Calendar/ActivityEditor.razor.js").Mode = JSRuntimeMode.Loose;
        var browser = new BrowserContext(context.JSInterop.JSRuntime);
        await browser.BrowserChanged(new("America/Chicago", "light"));
        context.Services.AddSingleton(browser);
        return context;
    }
}
