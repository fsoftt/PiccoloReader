using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests.ViewModels;

public class SettingsViewModelTests
{
    [Fact]
    public void Constructor_SavedCodeIsSpanish_SelectedLanguageCodeIsSpanish()
    {
        var fake = new FakeLanguagePreferenceService { SavedCode = "es" };

        var sut = new SettingsViewModel(fake, new FakeAdsPreferenceService());

        Assert.Equal("es", sut.SelectedLanguageCode);
    }

    [Fact]
    public void Constructor_NoSavedCode_DefaultsToEnglish()
    {
        var fake = new FakeLanguagePreferenceService { SavedCode = null };

        var sut = new SettingsViewModel(fake, new FakeAdsPreferenceService());

        Assert.Equal("en", sut.SelectedLanguageCode);
    }

    [Fact]
    public void SetLanguageCommand_SavesCodeAndUpdatesSelectedLanguageCode()
    {
        var fake = new FakeLanguagePreferenceService { SavedCode = "en" };
        var sut = new SettingsViewModel(fake, new FakeAdsPreferenceService());

        sut.SetLanguageCommand.Execute("es");

        Assert.Equal("es", fake.SavedCode);
        Assert.Equal("es", sut.SelectedLanguageCode);
    }

    [Fact]
    public void LanguageDisplayNames_And_LanguageCodes_AreParallelArrays()
    {
        var sut = new SettingsViewModel(new FakeLanguagePreferenceService(), new FakeAdsPreferenceService());

        Assert.Equal(sut.LanguageDisplayNames.Length, sut.LanguageCodes.Length);
        Assert.Equal("English", sut.LanguageDisplayNames[Array.IndexOf(sut.LanguageCodes, "en")]);
        Assert.Equal("Español", sut.LanguageDisplayNames[Array.IndexOf(sut.LanguageCodes, "es")]);
    }

    [Fact]
    public void Constructor_ReadsSupportWithAdsEnabledFromPreferenceService()
    {
        var fakeAds = new FakeAdsPreferenceService { Enabled = true };

        var sut = new SettingsViewModel(new FakeLanguagePreferenceService(), fakeAds);

        Assert.True(sut.SupportWithAdsEnabled);
    }

    [Fact]
    public void SupportWithAdsEnabled_WhenChanged_PersistsToPreferenceService()
    {
        var fakeAds = new FakeAdsPreferenceService { Enabled = false };
        var sut = new SettingsViewModel(new FakeLanguagePreferenceService(), fakeAds);

        sut.SupportWithAdsEnabled = true;

        Assert.True(fakeAds.Enabled);
    }
}
