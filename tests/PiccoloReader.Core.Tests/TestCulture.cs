using System.Globalization;
using System.Runtime.CompilerServices;

namespace PiccoloReader.Core.Tests;

// AppStrings resolves against CurrentUICulture, and MusicIconCatalog caches its
// localized names in a static list on first use. Pinning the invariant culture
// before any test runs keeps the expected (English) strings independent of the
// machine's locale.
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void PinInvariantCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }
}
