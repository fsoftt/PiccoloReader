using PiccoloReader.Core.Services.Piano;
using PiccoloReader.Core.Services.Tuner;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests.ViewModels;

public class PianoViewModelTests
{
    private readonly FakePianoService _service = new();
    private readonly FakePianoPreferenceService _prefs = new();
    private readonly FakeTunerPreferenceService _tunerPrefs = new()
    {
        Stored = new TunerSettings { NoteNaming = NoteNaming.Letter }
    };

    private PianoViewModel Create() => new(_service, _prefs, _tunerPrefs);

    [Fact]
    public void Constructor_DefaultsToTenWhiteKeysFromC4InPortrait()
    {
        var sut = Create();

        Assert.Equal(10, sut.WhiteKeyCount);
        Assert.Equal(60, sut.FirstWhiteKey);
        Assert.Equal("grand", sut.Instrument.Id);
        Assert.Equal("grand", _service.Instrument?.Id);
        Assert.False(sut.SustainOn);
    }

    [Fact]
    public void IsLandscape_UsesItsOwnKeyCountAndRange()
    {
        var sut = Create();

        sut.IsLandscape = true;

        Assert.Equal(17, sut.WhiteKeyCount);
        Assert.Equal(48, sut.FirstWhiteKey);
    }

    [Fact]
    public void KeyCount_IsStoredPerOrientationAndClamped()
    {
        var sut = Create();

        sut.KeysPlusCommand.Execute(null);
        sut.IsLandscape = true;
        sut.KeysMinusCommand.Execute(null);

        Assert.Equal(16, sut.WhiteKeyCount);
        sut.IsLandscape = false;
        Assert.Equal(11, sut.WhiteKeyCount);

        for (var i = 0; i < 50; i++)
        {
            sut.KeysMinusCommand.Execute(null);
        }

        Assert.Equal(PianoKeyboardLayout.MinWhiteKeys, sut.WhiteKeyCount);
        for (var i = 0; i < 50; i++)
        {
            sut.KeysPlusCommand.Execute(null);
        }

        Assert.Equal(PianoKeyboardLayout.MaxWhiteKeys, sut.WhiteKeyCount);
    }

    [Fact]
    public void Octave_ShiftsBySevenWhiteKeysAndClampsAtTheEnds()
    {
        var sut = Create();

        sut.OctaveUpCommand.Execute(null);
        Assert.Equal(72, sut.FirstWhiteKey);

        for (var i = 0; i < 20; i++)
        {
            sut.OctaveDownCommand.Execute(null);
        }

        Assert.Equal(21, sut.FirstWhiteKey);
    }

    [Fact]
    public void KeysPlus_AtTheTopOfTheKeyboard_ShiftsTheWindowLeft()
    {
        var sut = Create();
        sut.SetFirstWhiteKey(200);

        sut.KeysPlusCommand.Execute(null);

        var layout = new PianoKeyboardLayout(sut.FirstWhiteKey, sut.WhiteKeyCount, 100f, 10f);
        Assert.Equal(108, layout.LastWhiteKey);
    }

    [Fact]
    public void CycleLabels_GoesOffCOnlyAllAndAround()
    {
        _prefs.Stored = new PianoSettings { Labels = PianoLabels.Off };
        var sut = Create();

        sut.CycleLabelsCommand.Execute(null);
        Assert.Equal(PianoLabels.COnly, sut.Labels);
        sut.CycleLabelsCommand.Execute(null);
        Assert.Equal(PianoLabels.All, sut.Labels);
        sut.CycleLabelsCommand.Execute(null);
        Assert.Equal(PianoLabels.Off, sut.Labels);
    }

    [Fact]
    public void ToggleSustain_ForwardsToTheService()
    {
        var sut = Create();

        sut.ToggleSustainCommand.Execute(null);
        Assert.True(_service.Sustain);
        sut.ToggleSustainCommand.Execute(null);
        Assert.False(_service.Sustain);
    }

    [Fact]
    public void Readout_ShowsHeldNotesLowToHighAndStaysDimmedAfterRelease()
    {
        var sut = Create();

        sut.NoteOn(67, 100);
        sut.NoteOn(60, 100);
        sut.NoteOn(64, 100);

        Assert.Equal("C4 · E4 · G4", sut.ReadoutText);
        Assert.False(sut.ReadoutIsDim);

        sut.NoteOff(60);
        sut.NoteOff(64);
        sut.NoteOff(67);

        Assert.Equal("G4", sut.ReadoutText);
        Assert.True(sut.ReadoutIsDim);
        Assert.Equal([67, 60, 64], _service.NotesOn);
        Assert.Equal([60, 64, 67], _service.NotesOff);
    }

    [Fact]
    public void Readout_ShowsAtMostFourNotes()
    {
        var sut = Create();

        foreach (var note in new[] { 60, 62, 64, 65, 67 })
        {
            sut.NoteOn(note, 100);
        }

        Assert.Equal("C4 · D4 · E4 · F4", sut.ReadoutText);
    }

    [Fact]
    public void Readout_UsesTheTunerNotationPreference()
    {
        _tunerPrefs.Stored = new TunerSettings { NoteNaming = NoteNaming.Solfege };
        var sut = Create();
        sut.Attach();

        sut.NoteOn(69, 100);

        Assert.Equal("La4", sut.ReadoutText);
    }

    [Fact]
    public void AttachAndDetach_TakeAndReturnOneLease_AndReleaseHeldNotes()
    {
        var sut = Create();

        sut.Attach();
        sut.Attach();
        sut.NoteOn(60, 100);
        sut.ToggleSustainCommand.Execute(null);
        sut.Detach();
        sut.Detach();

        Assert.Equal(0, _service.Leases);
        Assert.Equal([60], _service.NotesOff);
        Assert.False(sut.SustainOn);
    }

    [Fact]
    public void Loading_FollowsTheServiceState()
    {
        _service.State = PianoLoadState.Loading;
        var sut = Create();
        Assert.True(sut.IsLoading);

        _service.RaiseStateChanged(PianoLoadState.Ready);
        Assert.False(sut.IsLoading);

        _service.RaiseStateChanged(PianoLoadState.Failed);
        Assert.True(sut.LoadFailed);
        Assert.False(sut.IsLoading);
    }

    [Fact]
    public void SelectInstrument_UpdatesTheServiceAndIsPersisted()
    {
        var sut = Create();

        sut.SelectInstrument(PianoInstruments.ById("flute"));
        sut.PersistIfDirty();

        Assert.Equal("flute", _service.Instrument?.Id);
        Assert.Equal("flute", _prefs.Stored.InstrumentId);
    }

    [Fact]
    public void PersistIfDirty_SavesOnlyWhenSomethingChanged()
    {
        var sut = Create();

        sut.PersistIfDirty();
        Assert.Equal(0, _prefs.SaveCount);

        sut.KeysPlusCommand.Execute(null);
        sut.PersistIfDirty();
        sut.PersistIfDirty();
        Assert.Equal(1, _prefs.SaveCount);
    }

    [Fact]
    public void Settings_RoundTripThroughTheStore()
    {
        var first = Create();
        first.SelectInstrument(PianoInstruments.ById("cello"));
        first.KeysPlusCommand.Execute(null);
        first.OctaveUpCommand.Execute(null);
        first.CycleLabelsCommand.Execute(null);
        first.IsLandscape = true;
        first.KeysMinusCommand.Execute(null);
        first.PersistIfDirty();

        var second = Create();

        Assert.Equal("cello", second.Instrument.Id);
        Assert.Equal(PianoLabels.All, second.Labels);
        Assert.Equal(11, second.WhiteKeyCount);
        Assert.Equal(72, second.FirstWhiteKey);
        second.IsLandscape = true;
        Assert.Equal(16, second.WhiteKeyCount);
    }

    [Fact]
    public void Constructor_OutOfRangeStoredValues_AreClamped()
    {
        _prefs.Stored = new PianoSettings
        {
            InstrumentId = "gone",
            PortraitWhiteKeys = 500,
            PortraitFirstKey = 999,
            Labels = (PianoLabels)42
        };

        var sut = Create();

        Assert.Equal("grand", sut.Instrument.Id);
        Assert.Equal(PianoKeyboardLayout.MaxWhiteKeys, sut.WhiteKeyCount);
        Assert.Equal(PianoLabels.COnly, sut.Labels);
        var layout = new PianoKeyboardLayout(sut.FirstWhiteKey, sut.WhiteKeyCount, 100f, 10f);
        Assert.Equal(108, layout.LastWhiteKey);
    }
}
