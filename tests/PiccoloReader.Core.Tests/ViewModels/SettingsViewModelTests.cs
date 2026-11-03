using PiccoloReader.Core.Services.Stylus;
using PiccoloReader.Core.Services.Tuner;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests.ViewModels;

public class SettingsViewModelTests
{
    [Fact]
    public void Constructor_SavedCodeIsSpanish_SelectedLanguageCodeIsSpanish()
    {
        var fake = new FakeLanguagePreferenceService { SavedCode = "es" };

        var sut = new SettingsViewModel(fake, new FakeTunerStripPreferenceService(), new FakeStylusPreferenceService(), new FakeConcertModePreferenceService());

        Assert.Equal("es", sut.SelectedLanguageCode);
    }

    [Fact]
    public void Constructor_NoSavedCode_DefaultsToEnglish()
    {
        var fake = new FakeLanguagePreferenceService { SavedCode = null };

        var sut = new SettingsViewModel(fake, new FakeTunerStripPreferenceService(), new FakeStylusPreferenceService(), new FakeConcertModePreferenceService());

        Assert.Equal("en", sut.SelectedLanguageCode);
    }

    [Fact]
    public void SetLanguageCommand_SavesCodeAndUpdatesSelectedLanguageCode()
    {
        var fake = new FakeLanguagePreferenceService { SavedCode = "en" };
        var sut = new SettingsViewModel(fake, new FakeTunerStripPreferenceService(), new FakeStylusPreferenceService(), new FakeConcertModePreferenceService());

        sut.SetLanguageCommand.Execute("es");

        Assert.Equal("es", fake.SavedCode);
        Assert.Equal("es", sut.SelectedLanguageCode);
    }

    [Fact]
    public void LanguageDisplayNames_And_LanguageCodes_AreParallelArrays()
    {
        var sut = new SettingsViewModel(new FakeLanguagePreferenceService(), new FakeTunerStripPreferenceService(), new FakeStylusPreferenceService(), new FakeConcertModePreferenceService());

        Assert.Equal(sut.LanguageDisplayNames.Length, sut.LanguageCodes.Length);
        Assert.Equal("English", sut.LanguageDisplayNames[Array.IndexOf(sut.LanguageCodes, "en")]);
        Assert.Equal("Español", sut.LanguageDisplayNames[Array.IndexOf(sut.LanguageCodes, "es")]);
    }

    [Fact]
    public void Constructor_ReadsTunerStripPositionFromPreferenceService()
    {
        var fakeStrip = new FakeTunerStripPreferenceService { Position = TunerStripPosition.Top };

        var sut = new SettingsViewModel(new FakeLanguagePreferenceService(), fakeStrip, new FakeStylusPreferenceService(), new FakeConcertModePreferenceService());

        Assert.Equal(TunerStripPosition.Top, sut.TunerStripPosition);
    }

    [Fact]
    public void TunerStripPosition_DefaultsToRight()
    {
        var sut = new SettingsViewModel(new FakeLanguagePreferenceService(), new FakeTunerStripPreferenceService(), new FakeStylusPreferenceService(), new FakeConcertModePreferenceService());

        Assert.Equal(TunerStripPosition.Right, sut.TunerStripPosition);
    }

    [Fact]
    public void TunerStripPosition_WhenChanged_PersistsToPreferenceService()
    {
        var fakeStrip = new FakeTunerStripPreferenceService();
        var sut = new SettingsViewModel(new FakeLanguagePreferenceService(), fakeStrip, new FakeStylusPreferenceService(), new FakeConcertModePreferenceService());

        sut.TunerStripPosition = TunerStripPosition.Bottom;

        Assert.Equal(TunerStripPosition.Bottom, fakeStrip.Position);
    }

    [Fact]
    public void TunerStripPositionNames_AreParallelToPositions()
    {
        var sut = new SettingsViewModel(new FakeLanguagePreferenceService(), new FakeTunerStripPreferenceService(), new FakeStylusPreferenceService(), new FakeConcertModePreferenceService());

        Assert.Equal(sut.TunerStripPositions.Length, sut.TunerStripPositionNames.Length);
        Assert.Equal(4, sut.TunerStripPositions.Distinct().Count());
        Assert.All(sut.TunerStripPositionNames, name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }

    [Fact]
    public void StylusDrawsDirectly_DefaultsOnAndPersistsChanges()
    {
        var fakeStylus = new FakeStylusPreferenceService();
        var sut = new SettingsViewModel(new FakeLanguagePreferenceService(), new FakeTunerStripPreferenceService(), fakeStylus, new FakeConcertModePreferenceService());

        Assert.True(sut.StylusDrawsDirectly);

        sut.StylusDrawsDirectly = false;

        Assert.False(fakeStylus.DrawsDirectly);
    }

    [Fact]
    public void ConcertModeEnabled_DefaultsOnAndPersistsChanges()
    {
        var fakeConcert = new FakeConcertModePreferenceService();
        var sut = new SettingsViewModel(new FakeLanguagePreferenceService(), new FakeTunerStripPreferenceService(), new FakeStylusPreferenceService(), fakeConcert);

        Assert.True(sut.ConcertModeEnabled);

        sut.ConcertModeEnabled = false;

        Assert.False(fakeConcert.Enabled);
    }

    [Fact]
    public void Constructor_ConcertModeSavedOff_StartsOff()
    {
        var sut = new SettingsViewModel(new FakeLanguagePreferenceService(), new FakeTunerStripPreferenceService(), new FakeStylusPreferenceService(), new FakeConcertModePreferenceService { Enabled = false });

        Assert.False(sut.ConcertModeEnabled);
    }
}
