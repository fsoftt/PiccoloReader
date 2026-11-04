using PiccoloReader.Core.Services.Concert;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests.ViewModels;

public class ConcertTutorialTests
{
    private static SettingsViewModel Create(FakeConcertTutorialPreferenceService tutorial) =>
        new(new FakeLanguagePreferenceService(), new FakeTunerStripPreferenceService(), new FakeStylusPreferenceService(), new FakeConcertModePreferenceService(), tutorial);

    [Fact]
    public void Switch_DefaultsOn_WhenNothingSeen()
    {
        Assert.True(Create(new FakeConcertTutorialPreferenceService()).ConcertTutorialEnabled);
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    public void Switch_ReflectsFlags(bool library, bool viewer, bool expected)
    {
        var sut = Create(new FakeConcertTutorialPreferenceService { LibrarySeen = library, ViewerSeen = viewer });

        Assert.Equal(expected, sut.ConcertTutorialEnabled);
    }

    [Fact]
    public void TurningOn_ResetsBothStages()
    {
        var prefs = new FakeConcertTutorialPreferenceService { LibrarySeen = true, ViewerSeen = true };
        var sut = Create(prefs);
        Assert.False(sut.ConcertTutorialEnabled);

        sut.ConcertTutorialEnabled = true;

        Assert.Equal(1, prefs.ResetCount);
        Assert.False(prefs.LibrarySeen);
        Assert.False(prefs.ViewerSeen);
    }

    [Fact]
    public void TurningOff_MarksBothStagesSeen()
    {
        var prefs = new FakeConcertTutorialPreferenceService();
        var sut = Create(prefs);

        sut.ConcertTutorialEnabled = false;

        Assert.True(prefs.LibrarySeen);
        Assert.True(prefs.ViewerSeen);
        Assert.Equal(0, prefs.ResetCount);
    }

    [Fact]
    public void Refresh_PicksUpFlagsChangedElsewhere_WithoutWritingThemBack()
    {
        var prefs = new FakeConcertTutorialPreferenceService();
        var sut = Create(prefs);

        prefs.LibrarySeen = true;
        prefs.ViewerSeen = true;
        sut.RefreshConcertTutorial();

        Assert.False(sut.ConcertTutorialEnabled);
        Assert.Equal(0, prefs.ResetCount);

        prefs.ViewerSeen = false;
        sut.RefreshConcertTutorial();

        Assert.True(sut.ConcertTutorialEnabled);
        Assert.Equal(0, prefs.ResetCount);
        Assert.True(prefs.LibrarySeen);
    }

    [Fact]
    public void FinishingOneStage_KeepsSwitchOn()
    {
        var prefs = new FakeConcertTutorialPreferenceService();
        var sut = Create(prefs);

        prefs.LibrarySeen = true;
        sut.RefreshConcertTutorial();

        Assert.True(sut.ConcertTutorialEnabled);
    }

    [Theory]
    [InlineData(true, false, true, true, true)]
    [InlineData(false, false, true, true, false)]
    [InlineData(true, true, true, true, false)]
    [InlineData(true, false, false, true, false)]
    [InlineData(true, false, true, false, false)]
    public void LibraryStage_OnlyWhenEverythingLinesUp(bool concert, bool seen, bool visible, bool handles, bool expected)
    {
        Assert.Equal(expected, ConcertTutorialPolicy.ShouldShowLibraryStage(concert, seen, visible, handles));
    }

    [Theory]
    [InlineData(true, false, true, true, true)]
    [InlineData(false, false, true, true, false)]
    [InlineData(true, true, true, true, false)]
    [InlineData(true, false, false, true, false)]
    [InlineData(true, false, true, false, false)]
    public void ViewerStage_OnlyWhenEverythingLinesUp(bool concert, bool seen, bool visible, bool neighbour, bool expected)
    {
        Assert.Equal(expected, ConcertTutorialPolicy.ShouldShowViewerStage(concert, seen, visible, neighbour));
    }
}
