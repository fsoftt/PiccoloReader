using System.Globalization;
using OpenQA.Selenium;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.E2E.Infrastructure;

namespace PiccoloReader.E2E.Tests;

/// <summary>Settings tab: language, ads switch, tutorial, legal rows.</summary>
[TestFixture]
public class SettingsTests : E2ETestBase
{
    protected override IReadOnlyList<SeedSheet> Sheets => Array.Empty<SeedSheet>();

    /// <summary>Opens the Settings tab (Shell tab: AutomationId when exposed, else its title).</summary>
    private static void OpenSettingsTab(string title)
    {
        if (Ui.TryFind("TabSettings") is { } tab)
        {
            tab.Click();
        }
        else
        {
            Ui.TapText(title);
        }

        Ui.Find("SettingsLanguageRow", 15);
    }

    private static string InCulture(string culture, Func<string> read)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            return read();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Test]
    public void Language_SwitchToSpanish_PersistsAndRelabelsTheUi()
    {
        OpenSettingsTab(AppStrings.Settings);
        Assert.That(Ui.TextOf("SettingsLanguageValue"), Is.EqualTo("English"));

        Ui.Tap("SettingsLanguageRow");
        Ui.TapText("Español");
        // The "restart required" alert follows the change.
        Ui.FindText(AppStrings.RestartRequiredTitle);
        Ui.TapText(AppStrings.OK);
        Ui.Poll(() => AppSession.ReadStringPreference("AppLanguage") == "es", 10, "the language preference to be saved");

        AppSession.Relaunch();

        var settingsEs = InCulture("es", () => AppStrings.Settings);
        Assert.That(settingsEs, Is.EqualTo("Ajustes"), "sanity: Spanish resource");
        OpenSettingsTab(settingsEs);
        Assert.That(Ui.TextOf("SettingsLanguageValue"), Is.EqualTo("Español"));
        Assert.That(Ui.TextExists(InCulture("es", () => AppStrings.ShowTutorial)), Is.True, "labels are in Spanish");

        // And back to English.
        Ui.Tap("SettingsLanguageRow");
        Ui.TapText("English");
        Ui.TapText(InCulture("es", () => AppStrings.OK));
        Ui.Poll(() => AppSession.ReadStringPreference("AppLanguage") == "en", 10, "English saved again");
        AppSession.Relaunch();
        OpenSettingsTab(AppStrings.Settings);
        Assert.That(Ui.TextExists(AppStrings.ShowTutorial), Is.True, "labels are back in English");
    }

    [Test]
    public void AdsSwitch_PersistsAcrossRelaunch()
    {
        OpenSettingsTab(AppStrings.Settings);
        Assert.That(Ui.IsChecked("SettingsAdsSwitch"), Is.False, "ads are opt-in");

        Ui.Tap("SettingsAdsSwitch");
        Ui.Poll(() => Ui.IsChecked("SettingsAdsSwitch"), 10, "the switch to turn on");
        Ui.Poll(() => AppSession.ReadBoolPreference("SupportWithAds") == true, 10, "the preference to be saved");

        AppSession.Relaunch();
        OpenSettingsTab(AppStrings.Settings);

        Assert.That(Ui.IsChecked("SettingsAdsSwitch"), Is.True, "the switch is still on after a restart");
    }

    [Test]
    public void Tutorial_CanBeReopenedFromSettings()
    {
        OpenSettingsTab(AppStrings.Settings);

        Ui.Tap("SettingsTutorialRow");

        Ui.Find("TutorialCarousel", 15);
        Ui.Find("TutorialSkipButton");
        Ui.Tap("TutorialSkipButton");

        Ui.Find("SettingsTutorialRow", 15);
        Assert.That(Ui.Exists("TutorialCarousel"), Is.False, "the tutorial closes back to Settings");
    }

    [Test]
    public void LegalRows_Exist()
    {
        OpenSettingsTab(AppStrings.Settings);

        Assert.That(Ui.Exists("SettingsPrivacyRow"), Is.True);
        Assert.That(Ui.Exists("SettingsTermsRow"), Is.True);
    }
}
