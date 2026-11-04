using System.Globalization;
using PiccoloReader.E2E.Infrastructure;

namespace PiccoloReader.E2E;

/// <summary>Sets up the culture and disposes the shared Appium session once, around the whole run (root namespace = applies to every fixture).</summary>
[SetUpFixture]
public class E2ERunLifecycle
{
    // The app runs in English on the emulator; AppStrings must resolve to English too, whatever the host culture is.
    [OneTimeSetUp]
    public void UseEnglish()
    {
        var english = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentUICulture = english;
        CultureInfo.CurrentUICulture = english;
    }

    [OneTimeTearDown]
    public void Shutdown() => AppSession.Shutdown();
}
