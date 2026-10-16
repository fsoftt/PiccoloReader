using PiccoloReader.Core.Services.Metronome;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests.ViewModels;

public class MetronomeViewModelTests
{
    private readonly FakeMetronomePlayer _player = new();
    private readonly FakeMetronomePreferenceService _prefs = new();
    private long _now;

    private MetronomeViewModel Create() => new(_player, _prefs, () => _now);

    [Fact]
    public void Constructor_LoadsStoredSettingsAndPushesThemToPlayer()
    {
        _prefs.Stored = new MetronomeSettings { Bpm = 140, BeatsPerBar = 3, AccentFirstBeat = false, Subdivision = 2 };

        var sut = Create();

        Assert.Equal(140, sut.Bpm);
        Assert.Equal(3, sut.BeatsPerBar);
        Assert.False(sut.AccentFirstBeat);
        Assert.Equal(2, sut.Subdivision);
        Assert.Equal(_prefs.Stored, _player.Updates[^1]);
        Assert.Equal(0, _prefs.SaveCount);
    }

    [Fact]
    public void TogglePlay_StartsThenStops()
    {
        var sut = Create();

        sut.TogglePlayCommand.Execute(null);
        Assert.True(sut.IsPlaying);
        Assert.Equal(1, _player.StartCount);

        sut.TogglePlayCommand.Execute(null);
        Assert.False(sut.IsPlaying);
        Assert.Equal(1, _player.StopCount);
    }

    [Fact]
    public void IsPlaying_FollowsPlayerStoppingItself()
    {
        var sut = Create();
        sut.TogglePlayCommand.Execute(null);

        _player.Stop();

        Assert.False(sut.IsPlaying);
    }

    [Fact]
    public void IncrementAndDecrement_ChangeBpmAndStayInRange()
    {
        _prefs.Stored = new MetronomeSettings { Bpm = 250 };
        var sut = Create();

        sut.IncrementBpmCommand.Execute(null);
        Assert.Equal(250, sut.Bpm);

        sut.DecrementBpmCommand.Execute(null);
        Assert.Equal(249, sut.Bpm);

        sut.Bpm = 30;
        sut.DecrementBpmCommand.Execute(null);
        Assert.Equal(30, sut.Bpm);
    }

    [Fact]
    public void Bpm_OutOfRange_IsClampedAndSentToPlayerClamped()
    {
        var sut = Create();

        sut.Bpm = 1000;

        Assert.Equal(250, sut.Bpm);
        Assert.Equal(250, _player.Updates[^1].Bpm);
    }

    [Fact]
    public void BpmSlider_RoundsAndUpdatesBpm()
    {
        var sut = Create();
        var changed = new List<string?>();
        sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        sut.BpmSlider = 120.6;

        Assert.Equal(121, sut.Bpm);
        Assert.Contains(nameof(MetronomeViewModel.BpmSlider), changed);
    }

    [Fact]
    public void SettingChanges_AreSentToPlayerWhilePlaying()
    {
        var sut = Create();
        sut.TogglePlayCommand.Execute(null);

        sut.BeatsPerBar = 7;
        sut.Subdivision = 4;
        sut.AccentFirstBeat = false;

        var last = _player.Updates[^1];
        Assert.Equal(7, last.BeatsPerBar);
        Assert.Equal(4, last.Subdivision);
        Assert.False(last.AccentFirstBeat);
        Assert.Equal(1, _player.StartCount);
    }

    [Fact]
    public void BeatsPerBarAndSubdivision_AreClamped()
    {
        var sut = Create();

        sut.BeatsPerBar = 99;
        sut.Subdivision = 0;

        Assert.Equal(12, sut.BeatsPerBar);
        Assert.Equal(1, sut.Subdivision);
    }

    [Fact]
    public void Tap_SetsBpmFromSecondTap()
    {
        var sut = Create();

        _now = 10_000;
        sut.TapCommand.Execute(null);
        Assert.Equal(100, sut.Bpm);

        _now = 10_500;
        sut.TapCommand.Execute(null);
        Assert.Equal(120, sut.Bpm);
    }

    [Fact]
    public void AttachDetach_ControlsBeatSubscription()
    {
        var sut = Create();
        Assert.Equal(0, _player.BeatTickedSubscribers);

        sut.Attach();
        sut.Attach();
        Assert.Equal(1, _player.BeatTickedSubscribers);

        sut.Detach();
        Assert.Equal(0, _player.BeatTickedSubscribers);
    }

    [Fact]
    public void Beats_UpdateCurrentBeatOnlyWhileAttached_AndResetOnStop()
    {
        var sut = Create();
        sut.TogglePlayCommand.Execute(null);

        _player.RaiseBeat(2, false);
        Assert.Equal(-1, sut.CurrentBeat);

        sut.Attach();
        _player.RaiseBeat(0, true);
        Assert.Equal(0, sut.CurrentBeat);
        Assert.True(sut.CurrentBeatIsAccent);

        _player.RaiseBeat(2, false);
        Assert.Equal(2, sut.CurrentBeat);
        Assert.False(sut.CurrentBeatIsAccent);

        sut.TogglePlayCommand.Execute(null);
        Assert.Equal(-1, sut.CurrentBeat);
    }

    [Fact]
    public void Attach_SyncsIsPlayingWithPlayer()
    {
        var sut = Create();
        _player.Start();

        sut.Attach();

        Assert.True(sut.IsPlaying);
    }

    [Fact]
    public void Settings_PersistOnDetachOnlyWhenChanged()
    {
        var sut = Create();
        sut.Attach();
        sut.Detach();
        Assert.Equal(0, _prefs.SaveCount);

        sut.Attach();
        sut.Bpm = 90;
        sut.Bpm = 91;
        Assert.Equal(0, _prefs.SaveCount);

        sut.Detach();
        Assert.Equal(1, _prefs.SaveCount);
        Assert.Equal(91, _prefs.Stored.Bpm);
    }

    [Fact]
    public void Settings_PersistWhenPlaybackStops()
    {
        var sut = Create();
        sut.TogglePlayCommand.Execute(null);
        sut.BeatsPerBar = 5;

        _player.Stop();

        Assert.Equal(5, _prefs.Stored.BeatsPerBar);
    }
}
