using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests.ViewModels;

public class SettingsViewModelTests
{
    [Fact]
    public void Constructor_SavedCodeIsSpanish_SelectedLanguageCodeIsSpanish()
    {
        var fake = new FakeLanguagePreferenceService { SavedCode = "es" };

        var sut = new SettingsViewModel(fake);

        Assert.Equal("es", sut.SelectedLanguageCode);
    }

    [Fact]
    public void Constructor_NoSavedCode_DefaultsToEnglish()
    {
        var fake = new FakeLanguagePreferenceService { SavedCode = null };

        var sut = new SettingsViewModel(fake);

        Assert.Equal("en", sut.SelectedLanguageCode);
    }

    [Fact]
    public void SetLanguageCommand_SavesCodeAndUpdatesSelectedLanguageCode()
    {
        var fake = new FakeLanguagePreferenceService { SavedCode = "en" };
        var sut = new SettingsViewModel(fake);

        sut.SetLanguageCommand.Execute("es");

        Assert.Equal("es", fake.SavedCode);
        Assert.Equal("es", sut.SelectedLanguageCode);
    }
}
