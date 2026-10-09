using System.Text.RegularExpressions;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;

namespace PiccoloReader.E2E.Infrastructure;

/// <summary>A PDF to put in the library before the app starts (same effect as importing it).</summary>
public record SeedSheet(string Title, int Pages = 3, bool Landscape = false);

/// <summary>
/// Owns the single Appium (UiAutomator2) session shared by the whole run, and resets the app
/// to a known state between tests.
/// </summary>
public static class AppSession
{
    private const string PreferencesFile =
        "shared_prefs/" + E2EEnvironment.PackageName + ".microsoft.maui.essentials.preferences.xml";

    private static AndroidDriver? _driver;
    private static string? _templateDb;
    private static Exception? _templateError;

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
    /// Brings the app to a known state and starts it on the library: data wiped, tutorial done,
    /// portrait, the given sheets already in the library. The empty database (schema only) is
    /// captured once per run from a genuine first launch - which also exercises skipping the
    /// first-run tutorial through the UI - and then reused, which saves a splash + tutorial
    /// round trip per test.
    /// </summary>
    public static void ResetApp(
        string readingMode = "Horizontal",
        IReadOnlyList<SeedSheet>? sheets = null,
        string language = "en",
        bool? ads = null)
    {
        var template = EnsureTemplateDb();
        Wipe();

        // Preferences are left to the app (tutorial is skipped through the UI below); only a non-default
        // reading mode is pre-seeded, best effort.
        if (readingMode != "Horizontal" || ads is not null)
        {
            WritePreferences(tutorialCompleted: false, readingMode, language, ads);
        }

        var db = Path.Combine(Path.GetTempPath(), $"piccolo-e2e-seed-{Guid.NewGuid():N}.db3");
        File.Copy(template, db, overwrite: true);
        try
        {
            foreach (var sheet in sheets ?? Array.Empty<SeedSheet>())
            {
                var pdf = PdfFactory.CreateCached(sheet.Pages, sheet.Landscape);
                var storedName = $"{Guid.NewGuid():N}.pdf";
                Adb.PushToApp(pdf, $"files/Sheets/{storedName}");
                AppDb.InsertSheetOffline(db, sheet.Title, storedName);
            }

            Adb.PushToApp(db, AppDb.RelativePath);
        }
        finally
        {
            AppDb.DeleteTemp(db);
        }

        Launch();
        WaitForLibrary(skipTutorial: true);
        TestContext.Progress.WriteLine("prefs after launch: " + (ReadPreferencesXml() ?? "<none>").Replace((char)10, (char)32) + " ls=" + Adb.RunAs("ls shared_prefs", allowFailure: true).Replace((char)10, (char)32));
    }

    /// <summary>First launch of a wiped app: skip the tutorial through the UI, keep the resulting empty DB.</summary>
    private static string EnsureTemplateDb()
    {
        if (_templateDb is not null && File.Exists(_templateDb))
        {
            return _templateDb;
        }

        if (_templateError is not null)
        {
            throw new InvalidOperationException("The first-launch setup already failed in this run: " + _templateError.Message, _templateError);
        }

        try
        {
            CreateTemplateDb();
        }
        catch (Exception ex)
        {
            _templateError = ex;
            throw;
        }

        return _templateDb!;
    }

    private static void CreateTemplateDb()
    {
        _ = Driver; // installs the APK
        Wipe();
        WritePreferences(tutorialCompleted: false, "Horizontal", "en", null);
        Launch();
        WaitForLibrary(skipTutorial: true);
        Thread.Sleep(1500);
        Driver.TerminateApp(E2EEnvironment.PackageName);

        _templateDb = AppDb.PullToTemp();
    }

    private static void Wipe()
    {
        var driver = Driver;
        driver.TerminateApp(E2EEnvironment.PackageName);
        Adb.Shell($"pm clear {E2EEnvironment.PackageName}");

        // Stop the emulator from auto-rotating under us, and start every test in portrait.
        Adb.Shell("settings put system accelerometer_rotation 0");
        Adb.Shell("settings put system user_rotation 0");
        try { driver.Orientation = ScreenOrientation.Portrait; } catch { /* already portrait */ }
    }

    /// <summary>
    /// Waits for the library (past splash and, on a fresh install, the tutorial, which is skipped
    /// through its Skip button).
    /// </summary>
    public static void WaitForLibrary(bool skipTutorial = false)
    {
        Ui.Poll(() =>
        {
            if (Ui.Exists("LibraryImportButton"))
            {
                return true;
            }

            if (skipTutorial && Ui.TryFind("TutorialSkipButton") is { } skip)
            {
                skip.Click();
            }

            return false;
        }, 90, "the library to load");
    }

    public static void Launch()
    {
        Driver.ActivateApp(E2EEnvironment.PackageName);
    }

    /// <summary>Kills and restarts the app without touching its data (relaunch persistence checks).</summary>
    public static void Relaunch()
    {
        Driver.TerminateApp(E2EEnvironment.PackageName);
        Thread.Sleep(500);
        Launch();
        WaitForLibrary();
    }

    private static void WritePreferences(bool tutorialCompleted, string readingMode, string language, bool? ads)
    {
        // Same file/keys MAUI's Preferences API reads (see Maui*PreferenceService).
        var xml =
            "<?xml version='1.0' encoding='utf-8' standalone='yes' ?>\n" +
            "<map>\n" +
            (tutorialCompleted ? "  <boolean name=\"TutorialCompleted\" value=\"true\" />\n" : "") +
            (ads is { } a ? $"  <boolean name=\"SupportWithAds\" value=\"{(a ? "true" : "false")}\" />\n" : "") +
            $"  <string name=\"AppLanguage\">{language}</string>\n" +
            $"  <string name=\"ReadingMode\">{readingMode}</string>\n" +
            "</map>\n";
        Adb.WriteTextToApp(PreferencesFile, xml);
    }

    /// <summary>Reads the raw preferences file (null while the app has not written one).</summary>
    public static string? ReadPreferencesXml()
    {
        var all = Adb.RunAs("sh -c 'cat shared_prefs/*.xml'", allowFailure: true);
        return string.IsNullOrWhiteSpace(all) ? null : all;
    }

    /// <summary>Reads a string preference straight from the app's shared-prefs file.</summary>
    public static string? ReadStringPreference(string name)
    {
        var match = Regex.Match(
            ReadPreferencesXml() ?? "", $"<string name=\"{Regex.Escape(name)}\">([^<]*)</string>");
        return match.Success ? match.Groups[1].Value : null;
    }

    public static bool? ReadBoolPreference(string name)
    {
        var match = Regex.Match(
            ReadPreferencesXml() ?? "", $"<boolean name=\"{Regex.Escape(name)}\" value=\"(true|false)\"");
        return match.Success ? match.Groups[1].Value == "true" : null;
    }
}
