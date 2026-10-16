using System.Globalization;
using PiccoloReader.Core.Services.Tuner;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests.ViewModels;

public class TunerViewModelTests
{
    private readonly FakeTunerInput _input = new();
    private readonly FakeTunerPreferenceService _prefs = new();
    private readonly FakeMicrophonePermissionService _permission = new();
    private readonly FakeAdsPreferenceService _ads = new();

    private TunerViewModel Create(Action<Action>? post = null) => new(_input, _prefs, _permission, _ads, post);

    private static TunerReading Reading(double hz) => new(true, (float)hz, (float)(1200 * Math.Log2(hz / 440)));

    [Fact]
    public async Task Attach_PermissionDenied_ShowsNoPermissionAndDoesNotStart()
    {
        _permission.Granted = false;
        var sut = Create();

        await sut.AttachAsync();

        Assert.Equal(TunerState.NoPermission, sut.State);
        Assert.Equal(0, _input.StartCount);
        Assert.Equal(0, _input.SubscriberCount);
    }

    [Fact]
    public async Task Attach_StartsInputAndSubscribes()
    {
        var sut = Create();

        await sut.AttachAsync();
        await sut.AttachAsync();

        Assert.Equal(TunerState.Silent, sut.State);
        Assert.True(_input.IsRunning);
        Assert.Equal(1, _input.SubscriberCount);
    }

    [Theory]
    [InlineData(TunerStartResult.PermissionDenied, TunerState.NoPermission)]
    [InlineData(TunerStartResult.Unsupported, TunerState.Unsupported)]
    [InlineData(TunerStartResult.Failed, TunerState.Error)]
    public async Task Attach_StartFailure_MapsToState(TunerStartResult result, TunerState expected)
    {
        _input.StartResult = result;
        var sut = Create();

        await sut.AttachAsync();

        Assert.Equal(expected, sut.State);
        Assert.Equal(0, _input.SubscriberCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Attach_SetsShowAdsBannerFromPreference(bool enabled)
    {
        _ads.Enabled = enabled;
        var sut = Create();

        await sut.AttachAsync();

        Assert.Equal(enabled, sut.ShowAdsBanner);
    }

    [Fact]
    public async Task Detach_StopsInputUnsubscribesAndPersistsWhenDirty()
    {
        var sut = Create();
        await sut.AttachAsync();
        sut.A4Hz = 442;

        sut.Detach();

        Assert.Equal(1, _input.StopCount);
        Assert.Equal(0, _input.SubscriberCount);
        Assert.Equal(TunerState.Idle, sut.State);
        Assert.Equal(1, _prefs.SaveCount);
        Assert.Equal(442, _prefs.Stored.A4Hz);
    }

    [Fact]
    public async Task Detach_WhenNothingChanged_DoesNotSave()
    {
        var sut = Create();
        await sut.AttachAsync();

        sut.Detach();

        Assert.Equal(0, _prefs.SaveCount);
    }

    [Fact]
    public async Task Reading_UpdatesNoteCentsAndInTune()
    {
        var sut = Create();
        await sut.AttachAsync();

        _input.Raise(Reading(880 * Math.Pow(2, 3 / 1200.0)));

        Assert.Equal(TunerState.Listening, sut.State);
        Assert.Equal("A", sut.NoteName);
        Assert.Equal(5, sut.Octave);
        Assert.Equal(3f, sut.Cents, 0.1f);
        Assert.True(sut.IsInTune);
        Assert.Equal(881.5f, sut.FrequencyHz, 0.1f);

        _input.Raise(Reading(880 * Math.Pow(2, -20 / 1200.0)));

        Assert.Equal(-20f, sut.Cents, 0.1f);
        Assert.False(sut.IsInTune);
    }

    [Fact]
    public async Task NoPitchReading_GoesSilent()
    {
        var sut = Create();
        await sut.AttachAsync();
        _input.Raise(Reading(880));

        _input.Raise(default);

        Assert.Equal(TunerState.Silent, sut.State);
        Assert.Equal(string.Empty, sut.NoteName);
        Assert.False(sut.IsInTune);
    }

    [Fact]
    public async Task Reading_UsesPostAndCoalescesPendingUpdates()
    {
        var queue = new List<Action>();
        var sut = Create(queue.Add);
        await sut.AttachAsync();

        _input.Raise(Reading(880));
        _input.Raise(Reading(1046.5));

        Assert.Single(queue);
        Assert.Equal(string.Empty, sut.NoteName);
        queue[0]();
        Assert.Equal("C", sut.NoteName);
        Assert.Equal(6, sut.Octave);
    }

    [Fact]
    public async Task Reading_AfterDetach_IsIgnored()
    {
        var queue = new List<Action>();
        var sut = Create(queue.Add);
        await sut.AttachAsync();
        _input.Raise(Reading(880));

        sut.Detach();
        queue[0]();

        Assert.Equal(TunerState.Idle, sut.State);
        Assert.Equal(string.Empty, sut.NoteName);
    }

    [Fact]
    public async Task Reading_RaisesPropertyChangedOnlyForChangedValues()
    {
        var sut = Create();
        await sut.AttachAsync();
        _input.Raise(Reading(880));
        var changed = new List<string?>();
        sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _input.Raise(Reading(880));

        Assert.Empty(changed);
    }

    [Fact]
    public void A4_ClampsAndForwardsToInput()
    {
        _prefs.Stored = new TunerSettings { A4Hz = 450 };
        var sut = Create();

        sut.IncreaseA4Command.Execute(null);
        Assert.Equal(450, sut.A4Hz);

        sut.DecreaseA4Command.Execute(null);
        Assert.Equal(449, sut.A4Hz);
        Assert.Equal(449, _input.Updates[^1].A4Hz);

        sut.A4Hz = 100;
        Assert.Equal(430, sut.A4Hz);
        Assert.Equal(430, _input.Updates[^1].A4Hz);
    }

    [Theory]
    [InlineData(440)]
    [InlineData(442)]
    [InlineData(443)]
    public void SetA4Preset_AppliesPreset(int hz)
    {
        var sut = Create();

        sut.SetA4PresetCommand.Execute(hz);

        Assert.Equal(hz, sut.A4Hz);
    }

    [Fact]
    public void Constructor_LoadsStoredSettingsAndPushesThemToInput()
    {
        _prefs.Stored = new TunerSettings { A4Hz = 443, ShowAsWritten = true, NoteNaming = NoteNaming.Solfege };

        var sut = Create();

        Assert.Equal(443, sut.A4Hz);
        Assert.True(sut.ShowAsWritten);
        Assert.Equal(NoteNaming.Solfege, sut.NoteNaming);
        Assert.Equal(_prefs.Stored, _input.Updates[^1]);
        Assert.Equal(0, _prefs.SaveCount);
    }

    [Fact]
    public async Task SettingChanges_RemapTheCurrentNoteAndMarkDirty()
    {
        var sut = Create();
        await sut.AttachAsync();
        _input.Raise(Reading(880));

        sut.ShowAsWritten = true;
        Assert.Equal(4, sut.Octave);
        Assert.True(_input.Updates[^1].ShowAsWritten);

        sut.NoteNaming = NoteNaming.Solfege;
        Assert.Equal("La", sut.NoteName);

        sut.PersistIfDirty();
        Assert.Equal(1, _prefs.SaveCount);
        sut.PersistIfDirty();
        Assert.Equal(1, _prefs.SaveCount);
    }

    [Fact]
    public async Task AutoNaming_FollowsUiCulture()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es");
            var sut = Create();
            await sut.AttachAsync();

            _input.Raise(Reading(880));

            Assert.Equal("La", sut.NoteName);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void OpenSettings_ForwardsToPermissionService()
    {
        var sut = Create();

        sut.OpenSettingsCommand.Execute(null);

        Assert.Equal(1, _permission.OpenSettingsCount);
    }
}
