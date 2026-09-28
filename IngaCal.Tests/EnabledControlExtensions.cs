using AngleSharp.Dom;
using Bunit;

namespace IngaCal.Tests;

internal static class EnabledControlExtensions
{
    // bUnit dispatches click callbacks even when native HTML would suppress a disabled control.
    public static void ClickEnabled(this IElement element)
    {
        Assert.False(element.HasAttribute("disabled"), $"Expected an enabled control: {element.OuterHtml}");
        element.Click();
    }

    public static void SubmitWithEnabledButton(this IElement form)
    {
        var submit = form.QuerySelector("button[type=submit]");
        Assert.NotNull(submit);
        Assert.False(submit.HasAttribute("disabled"), $"Expected an enabled submit button: {submit.OuterHtml}");
        form.Submit();
    }
}
