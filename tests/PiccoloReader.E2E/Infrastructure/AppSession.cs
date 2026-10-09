using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;

namespace PiccoloReader.E2E.Infrastructure;

/// <summary>
/// Owns the single Appium (UiAutomator2) session shared by the whole run, and resets the app
/// to a known state between tests.
/// </summary>
public static class AppSession
{
    private const string PreferencesFile =
        "shared_prefs/" + E2EEnvironment.PackageName + ".microsoft.maui.essentials.preferences.xml";

    private static AndroidDriver? _driver;

    public static AndroidDriver Driver => _driver ??= Create();

    private static AndroidDriver Create()
    {
        var options = new AppiumOptions
        {
            PlatformName = "Android",
            AutomationName = "UiAutomator2",
            App = E2EEnvironment.ApkPath
        };

        // The app is (re)launched explicitly by ResetApp; the session only installs it.
        options.AddAdditionalAppiumOption("appPackage", E2EEnvironment.PackageName);
        options.AddAdditionalAppiumOption("autoLaunch", false);
        options.AddAdditionalAppiumOption("noReset", true);
        options.AddAdditionalAppiumOption("fullReset", false);
        options.AddAdditionalAppiumOption("newCommandTimeout", 900);
        options.AddAdditionalAppiumOption("uiautomator2ServerInstallTimeout", 180_000);
        options.AddAdditionalAppiumOption("uiautomator2ServerLaunchTimeout", 180_000);
        options.AddAdditionalAppiumOption("adbExecTimeout", 180_000);
        options.AddAdditionalAppiumOption("androidInstallTimeout", 300_000);
        options.AddAdditionalAppiumOption("disableWindowAnimation", true);
        options.AddAdditionalAppiumOption("ignoreHiddenApiPolicyError", true);
        // A MAUI app with animations is rarely "idle" for UiAutomator, which would make every
        // command wait out its idle timeout. Polling is done explicitly by the tests instead.
        options.AddAdditionalAppiumOption("settings[waitForIdleTimeout]", 0);
        options.AddAdditionalAppiumOption("settings[waitForSelectorTimeout]", 0);

        var driver = new AndroidDriver(E2EEnvironment.AppiumUrl, options, TimeSpan.FromMinutes(5));
        driver.Manage().Timeouts().ImplicitWait = TimeSpan.Zero;
        return driver;
    }

    public static void Shutdown()
    {
        try { _driver?.Quit(); } catch { /* the session may already be gone */ }
        _driver = null;
    }

    /// <summary>
    /// Brings the app to a fresh first-launch state (empty library, tutorial skipped, English UI,
    /// portrait) and starts it. <paramref name="readingMode"/> pre-seeds the saved reading mode.
    /// </summary>
    public static void ResetApp(string readingMode = "Horizontal")
    {
        var driver = Driver;
        driver.TerminateApp(E2EEnvironment.PackageName);

        if (E2EEnvironment.ResetWithPmClear)
        {
            Adb.Shell($"pm clear {E2EEnvironment.PackageName}");
        }

        // Stop the emulator from auto-rotating under us, and start every test in portrait.
        Adb.Shell("settings put system accelerometer_rotation 0");
        Adb.Shell("settings put system user_rotation 0");
        driver.Orientation = ScreenOrientation.Portrait;

        WritePreferences(readingMode);
        Launch();
    }

    public static void Launch()
    {
        Driver.ActivateApp(E2EEnvironment.PackageName);
    }

    /// <summary>Kills and restarts the app without touching its data (relaunch persistence checks).</summary>
    public static void Relaunch()
    {
        Driver.TerminateApp(E2EEnvironment.PackageName);
        Launch();
    }

    private static void WritePreferences(string readingMode)
    {
        // Same file/keys MAUI's Preferences API reads (see Maui*PreferenceService).
        var xml =
            "<?xml version='1.0' encoding='utf-8' standalone='yes' ?>\n" +
            "<map>\n" +
            "  <boolean name=\"TutorialCompleted\" value=\"true\" />\n" +
            "  <string name=\"AppLanguage\">en</string>\n" +
            $"  <string name=\"ReadingMode\">{readingMode}</string>\n" +
            "</map>\n";
        Adb.WriteTextToApp(PreferencesFile, xml);
    }

    /// <summary>Reads a string preference straight from the app's shared-prefs file.</summary>
    public static string? ReadStringPreference(string name)
    {
        var xml = Adb.ReadTextFromApp(PreferencesFile);
        if (xml is null)
        {
            return null;
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            xml, $"<string name=\"{System.Text.RegularExpressions.Regex.Escape(name)}\">([^<]*)</string>");
        return match.Success ? match.Groups[1].Value : null;
    }
}
