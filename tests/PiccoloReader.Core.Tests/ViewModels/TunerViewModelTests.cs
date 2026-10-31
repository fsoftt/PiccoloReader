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
    public async Task Detach_StopsInputAndUnsubscribes()
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
    public async Task SettingChange_IsSavedImmediately_AndDetachDoesNotSaveAgain()
    {
        var sut = Create();
        await sut.AttachAsync();

        sut.A4Hz = 442;
        Assert.Equal(1, _prefs.SaveCount);
        Assert.Equal(442, _prefs.Stored.A4Hz);

        sut.OctaveShift = -1;
        Assert.Equal(2, _prefs.SaveCount);
        Assert.Equal(-1, _prefs.Stored.OctaveShift);

        sut.NoteNaming = NoteNaming.Solfege;
        Assert.Equal(3, _prefs.SaveCount);
        Assert.Equal(NoteNaming.Solfege, _prefs.Stored.NoteNaming);

        sut.Detach();
        Assert.Equal(3, _prefs.SaveCount);
    }

    [Fact]
    public void PersistIfDirty_RetriesAfterAFailedSave()
    {
        var failing = new ThrowingOncePreferenceService();
        var sut = new TunerViewModel(_input, failing, _permission, _ads);

        sut.A4Hz = 442;
        Assert.NotEqual(442, failing.Stored.A4Hz);

        sut.PersistIfDirty();
        Assert.Equal(442, failing.Stored.A4Hz);
    }

    private sealed class ThrowingOncePreferenceService : ITunerPreferenceService
    {
        private bool _thrown;

        public TunerSettings Stored { get; private set; } = new();

        public TunerSettings Load() => Stored;

        public void Save(TunerSettings settings)
        {
            if (!_thrown)
            {
                _thrown = true;
                throw new InvalidOperationException();
            }

            Stored = settings;
        }
    }

    [Fact]
    public async Task Detach_WhileInputIsOpening_KeepsTheViewModelIdle()
    {
        using var release = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        var input = new BlockingStartInput(entered, release);
        var sut = new TunerViewModel(input, _prefs, _permission, _ads);

        var attach = sut.AttachAsync();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

        sut.Detach();
        release.Set();
        await attach;

        Assert.Equal(TunerState.Idle, sut.State);
        Assert.Equal(0, input.SubscriberCount);
        // Detach stops once; the late Start is then given the microphone back.
        Assert.Equal(2, input.StopCount);
        Assert.False(input.IsRunning);
    }

    private sealed class BlockingStartInput(ManualResetEventSlim entered, ManualResetEventSlim release) : FakeTunerInput
    {
        public override TunerStartResult Start()
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(5));
            return base.Start();
        }
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
        Assert.Equal("A", sut.NoteName);
        Assert.Equal(5, sut.Octave);
        Assert.Equal(0f, sut.Cents);
        Assert.Equal(0f, sut.FrequencyHz);
        Assert.False(sut.IsInTune);
    }

    [Fact]
    public async Task NoPitchReading_NeverShowsListeningWithZeroedValues()
    {
        var sut = Create();
        await sut.AttachAsync();
        _input.Raise(Reading(880));
        var transients = new List<string>();
        sut.PropertyChanged += (_, e) =>
        {
            var zeroed = e.PropertyName == nameof(TunerViewModel.FrequencyHz) && sut.FrequencyHz == 0f
                || e.PropertyName == nameof(TunerViewModel.Cents) && sut.Cents == 0f;
            if (zeroed && sut.State != TunerState.Silent)
            {
                transients.Add(e.PropertyName!);
            }
        };

        _input.Raise(default);

        Assert.Empty(transients);
        Assert.Equal(TunerState.Silent, sut.State);
        Assert.Equal(0f, sut.FrequencyHz);
    }

    [Fact]
    public async Task NewNoteAfterSilence_ReplacesHeldNote()
    {
        var sut = Create();
        await sut.AttachAsync();
        _input.Raise(Reading(880));
        _input.Raise(default);

        _input.Raise(Reading(1046.5));

        Assert.Equal(TunerState.Listening, sut.State);
        Assert.Equal("C", sut.NoteName);
        Assert.Equal(6, sut.Octave);
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
    public void A4_Clamps()
    {
        _prefs.Stored = new TunerSettings { A4Hz = 450 };
        var sut = Create();

        sut.IncreaseA4Command.Execute(null);
        Assert.Equal(450, sut.A4Hz);

        sut.DecreaseA4Command.Execute(null);
        Assert.Equal(449, sut.A4Hz);

        sut.A4Hz = 100;
        Assert.Equal(430, sut.A4Hz);
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
    public void Constructor_LoadsStoredSettings()
    {
        _prefs.Stored = new TunerSettings { A4Hz = 443, Key = TransposeKey.Eb, OctaveShift = 1, NoteNaming = NoteNaming.Solfege };

        var sut = Create();

        Assert.Equal(443, sut.A4Hz);
        Assert.Equal(TransposeKey.Eb, sut.TransposeKey);
        Assert.Equal(1, sut.OctaveShift);
        Assert.Equal(NoteNaming.Solfege, sut.NoteNaming);
        Assert.Equal(0, _prefs.SaveCount);
    }

    [Fact]
    public async Task SettingChanges_RemapTheCurrentNoteAndPersistImmediately()
    {
        var sut = Create();
        await sut.AttachAsync();
        _input.Raise(Reading(880));

        sut.OctaveShift = -1;
        Assert.Equal(4, sut.Octave);
        Assert.Equal(-1, sut.Settings.OctaveShift);

        sut.NoteNaming = NoteNaming.Solfege;
        Assert.Equal("La", sut.NoteName);

        Assert.Equal(2, _prefs.SaveCount);
        sut.PersistIfDirty();
        Assert.Equal(2, _prefs.SaveCount);
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

    [Fact]
    public async Task DetachThenAttach_DoesNotShowStaleNote()
    {
        var queue = new List<Action>();
        var sut = Create(queue.Add);
        await sut.AttachAsync();
        _input.Raise(Reading(880));
        sut.Detach();

        await sut.AttachAsync();
        foreach (var action in queue)
        {
            action();
        }

        Assert.Equal(TunerState.Silent, sut.State);
        Assert.Equal(string.Empty, sut.NoteName);
        Assert.Equal(0f, sut.FrequencyHz);
    }

    [Theory]
    [InlineData(TunerSettings.MaxA4Hz, true)]
    [InlineData(TunerSettings.MinA4Hz, false)]
    public void A4AtBoundary_IsNoOpAndNotDirty(int stored, bool increase)
    {
        _prefs.Stored = new TunerSettings { A4Hz = stored };
        var sut = Create();

        if (increase)
        {
            sut.IncreaseA4Command.Execute(null);
        }
        else
        {
            sut.DecreaseA4Command.Execute(null);
        }
        sut.PersistIfDirty();

        Assert.Equal(stored, sut.A4Hz);
        Assert.Equal(0, _prefs.SaveCount);
    }
}
