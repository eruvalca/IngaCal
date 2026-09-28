using System.Globalization;

namespace IngaCal.Tests;

internal sealed class InvariantCultureScope : IDisposable
{
    private readonly CultureInfo previous = CultureInfo.CurrentCulture;
    private readonly CultureInfo previousUi = CultureInfo.CurrentUICulture;

    public InvariantCultureScope()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = previous;
        CultureInfo.CurrentUICulture = previousUi;
    }
}
