using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PiccoloReader.Core.Services.Metronome;

namespace PiccoloReader.Core.ViewModels;

public partial class MetronomeViewModel : ObservableObject
{
    private readonly IMetronomePlayer _player;
    private readonly IMetronomePreferenceService _preferences;
    private readonly Func<long> _clock;
    private readonly TapTempo _tapTempo = new();
    private bool _isDirty;

    [ObservableProperty]
    private int _bpm;

    [ObservableProperty]
    private int _beatsPerBar;

    [ObservableProperty]
    private bool _accentFirstBeat;

    [ObservableProperty]
    private int _subdivision;

    [ObservableProperty]
    private bool _isPlaying;

    /// <summary>Beat (0-based, within the bar) being heard, or -1 when stopped.</summary>
    [ObservableProperty]
    private int _currentBeat = -1;

    [ObservableProperty]
    private bool _currentBeatIsAccent;

    public MetronomeViewModel(IMetronomePlayer player, IMetronomePreferenceService preferences, Func<long>? clock = null)
    {
        _player = player;
        _preferences = preferences;
        _clock = clock ?? (() => Environment.TickCount64);

        var settings = preferences.Load();
        _bpm = settings.Bpm;
        _beatsPerBar = settings.BeatsPerBar;
        _accentFirstBeat = settings.AccentFirstBeat;
        _subdivision = settings.Subdivision;
        _isPlaying = player.IsPlaying;
        player.UpdateSettings(settings);
        player.IsPlayingChanged += OnIsPlayingChanged;
    }

    public MetronomeSettings Settings => new()
    {
        Bpm = Bpm,
        BeatsPerBar = BeatsPerBar,
        AccentFirstBeat = AccentFirstBeat,
        Subdivision = Subdivision
    };

    /// <summary>Slider-friendly view of <see cref="Bpm"/>.</summary>
    public double BpmSlider
    {
        get => Bpm;
        set => Bpm = (int)Math.Round(value);
    }

    /// <summary>Starts following beats. Call when the metronome UI becomes visible.</summary>
    public void Attach()
    {
        _player.BeatTicked -= OnBeatTicked;
        _player.BeatTicked += OnBeatTicked;
        IsPlaying = _player.IsPlaying;
        if (!IsPlaying)
        {
            CurrentBeat = -1;
        }
    }

    /// <summary>Stops following beats so no UI work happens while hidden.</summary>
    public void Detach()
    {
        _player.BeatTicked -= OnBeatTicked;
        PersistIfDirty();
    }

    public void PersistIfDirty()
    {
        if (!_isDirty)
        {
            return;
        }

        _isDirty = false;
        _preferences.Save(Settings);
    }

    [RelayCommand]
    private void TogglePlay()
    {
        if (_player.IsPlaying)
        {
            _player.Stop();
        }
        else
        {
            _player.Start();
        }

        IsPlaying = _player.IsPlaying;
    }

    [RelayCommand]
    private void IncrementBpm() => Bpm++;

    [RelayCommand]
    private void DecrementBpm() => Bpm--;

    [RelayCommand]
    private void Tap()
    {
        if (_tapTempo.Tap(_clock()) is { } bpm)
        {
            Bpm = bpm;
        }
    }

    partial void OnBpmChanged(int value)
    {
        var clamped = Math.Clamp(value, MetronomeSettings.MinBpm, MetronomeSettings.MaxBpm);
        if (clamped != value)
        {
            Bpm = clamped;
            return;
        }

        OnPropertyChanged(nameof(BpmSlider));
        SettingsChanged();
    }

    partial void OnBeatsPerBarChanged(int value)
    {
        var clamped = Math.Clamp(value, MetronomeSettings.MinBeatsPerBar, MetronomeSettings.MaxBeatsPerBar);
        if (clamped != value)
        {
            BeatsPerBar = clamped;
            return;
        }

        SettingsChanged();
    }

    partial void OnSubdivisionChanged(int value)
    {
        var clamped = Math.Clamp(value, MetronomeSettings.MinSubdivision, MetronomeSettings.MaxSubdivision);
        if (clamped != value)
        {
            Subdivision = clamped;
            return;
        }

        SettingsChanged();
    }

    partial void OnAccentFirstBeatChanged(bool value) => SettingsChanged();

    private void SettingsChanged()
    {
        _isDirty = true;
        _player.UpdateSettings(Settings);
    }

    private void OnBeatTicked(int beatInBar, bool isAccent)
    {
        CurrentBeat = beatInBar;
        CurrentBeatIsAccent = isAccent;
    }

    private void OnIsPlayingChanged(object? sender, EventArgs e)
    {
        IsPlaying = _player.IsPlaying;
        if (!IsPlaying)
        {
            CurrentBeat = -1;
            PersistIfDirty();
        }
    }
}
