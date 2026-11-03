using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.Services.Piano;
using PiccoloReader.Core.Services.Tuner;

namespace PiccoloReader.Core.ViewModels;

/// <summary>
/// Piano tab state: instrument, visible key range (kept per orientation), labels, sustain and the
/// note readout. Touch input only calls <see cref="NoteOn"/>/<see cref="NoteOff"/>, which enqueue
/// to the audio engine and never wait for it.
/// </summary>
public partial class PianoViewModel : ObservableObject
{
    private const int MaxReadoutNotes = 4;
    private const string ReadoutSeparator = " · ";

    private readonly IPianoService _service;
    private readonly IPianoPreferenceService _preferences;
    private readonly ITunerPreferenceService _tunerPreferences;
    private readonly Action<Action> _post;
    private readonly List<int> _held = [];
    private readonly System.Text.StringBuilder _readoutBuilder = new();
    private readonly string?[] _noteNames = new string?[PianoKeyboardLayout.HighestNote + 1];
    private PianoSettings _settings;
    private NoteNaming _resolvedNaming;
    private int _portraitCount;
    private int _landscapeCount;
    private int _portraitFirst;
    private int _landscapeFirst;
    private bool _isDirty;
    private bool _attached;
    private bool _isLandscape;

    [ObservableProperty]
    private PianoInstrument _instrument;

    [ObservableProperty]
    private PianoLabels _labels;

    [ObservableProperty]
    private bool _sustainOn;

    /// <summary>The sounds are not ready yet: the keyboard is dimmed and ignores touches.</summary>
    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _loadFailed;

    [ObservableProperty]
    private string _readoutText = string.Empty;

    /// <summary>True while no key is down: the last notes stay on screen, dimmed.</summary>
    [ObservableProperty]
    private bool _readoutIsDim = true;

    /// <param name="post">
    /// Runs an action on the UI thread. The service reports its state from a pool thread; the
    /// default runs inline, which suits tests. It must be fire-and-forget.
    /// </param>
    public PianoViewModel(
        IPianoService service,
        IPianoPreferenceService preferences,
        ITunerPreferenceService tunerPreferences,
        Action<Action>? post = null)
    {
        _service = service;
        _preferences = preferences;
        _tunerPreferences = tunerPreferences;
        _post = post ?? (static action => action());

        _settings = preferences.Load();
        _instrument = PianoInstruments.ById(_settings.InstrumentId);
        _labels = Enum.IsDefined(_settings.Labels) ? _settings.Labels : PianoLabels.COnly;
        _portraitCount = PianoKeyboardLayout.ClampWhiteKeyCount(_settings.PortraitWhiteKeys);
        _landscapeCount = PianoKeyboardLayout.ClampWhiteKeyCount(_settings.LandscapeWhiteKeys);
        _portraitFirst = PianoKeyboardLayout.ClampFirstWhiteKey(_settings.PortraitFirstKey, _portraitCount);
        _landscapeFirst = PianoKeyboardLayout.ClampFirstWhiteKey(_settings.LandscapeFirstKey, _landscapeCount);
        _resolvedNaming = ResolveNaming();
        _readoutText = AppStrings.PianoReadoutHint;
        service.SetInstrument(_instrument);
        service.StateChanged += OnServiceStateChanged;
        ApplyServiceState();
    }

    public bool IsLandscape
    {
        get => _isLandscape;
        set
        {
            if (_isLandscape == value)
            {
                return;
            }

            _isLandscape = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FirstWhiteKey));
            OnPropertyChanged(nameof(WhiteKeyCount));
            OnPropertyChanged(nameof(KeysLabel));
            OnPropertyChanged(nameof(RangeLabel));
        }
    }

    /// <summary>First visible white key (MIDI) for the current orientation.</summary>
    public int FirstWhiteKey => IsLandscape ? _landscapeFirst : _portraitFirst;

    /// <summary>Visible white keys for the current orientation.</summary>
    public int WhiteKeyCount => IsLandscape ? _landscapeCount : _portraitCount;

    /// <summary>Visible range for the octave arrows, e.g. C4–E5 or Do4–Mi5.</summary>
    public string RangeLabel =>
        NoteName(FirstWhiteKey) + "–" + NoteName(PianoKeyboardLayout.LastWhiteKeyOf(FirstWhiteKey, WhiteKeyCount));

    public string KeysLabel => string.Format(CultureInfo.CurrentCulture, AppStrings.PianoKeysCountFormat, WhiteKeyCount);

    /// <summary>Naming used by the keyboard labels and the readout (the tuner's preference).</summary>
    public NoteNaming ResolvedNaming => _resolvedNaming;

    /// <summary>Takes the audio lease. Call when the screen becomes visible; it never blocks.</summary>
    public void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        var naming = ResolveNaming();
        if (naming != _resolvedNaming)
        {
            _resolvedNaming = naming;
            Array.Clear(_noteNames);
            OnPropertyChanged(nameof(ResolvedNaming));
            OnPropertyChanged(nameof(RangeLabel));
            RefreshReadout();
        }

        _service.Acquire();
        ApplyServiceState();
    }

    /// <summary>Lets go of every note and of the audio lease. Call when the screen is hidden.</summary>
    public void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        ReleaseAll();
        SustainOn = false;
        _service.Release();
        PersistIfDirty();
    }

    public void NoteOn(int note, int velocity)
    {
        if (!_held.Contains(note))
        {
            _held.Add(note);
            _held.Sort();
        }

        _service.NoteOn(note, velocity);
        RefreshReadout();
    }

    public void NoteOff(int note)
    {
        _held.Remove(note);
        _service.NoteOff(note);
        RefreshReadout();
    }

    /// <summary>Releases every key (focus lost, screen hidden, instrument changed).</summary>
    public void ReleaseAll()
    {
        foreach (var note in _held)
        {
            _service.NoteOff(note);
        }

        _held.Clear();
        RefreshReadout();
    }

    public void SelectInstrument(PianoInstrument instrument)
    {
        if (instrument == Instrument)
        {
            return;
        }

        // The engine releases the notes in flight, so the readout lets go of them too.
        ReleaseAll();
        Instrument = instrument;
        _service.SetInstrument(instrument);
        _isDirty = true;
    }

    /// <summary>Moves the window to start at this key (mini-map drag), clamped to the keyboard.</summary>
    public void SetFirstWhiteKey(int note)
    {
        var clamped = PianoKeyboardLayout.ClampFirstWhiteKey(note, WhiteKeyCount);
        if (clamped == FirstWhiteKey)
        {
            return;
        }

        if (IsLandscape)
        {
            _landscapeFirst = clamped;
        }
        else
        {
            _portraitFirst = clamped;
        }

        _isDirty = true;
        OnPropertyChanged(nameof(FirstWhiteKey));
        OnPropertyChanged(nameof(RangeLabel));
    }

    [RelayCommand]
    private void OctaveDown() => SetFirstWhiteKey(PianoKeyboardLayout.ShiftOctaves(FirstWhiteKey, WhiteKeyCount, -1));

    [RelayCommand]
    private void OctaveUp() => SetFirstWhiteKey(PianoKeyboardLayout.ShiftOctaves(FirstWhiteKey, WhiteKeyCount, 1));

    [RelayCommand]
    private void KeysMinus() => SetWhiteKeyCount(WhiteKeyCount - 1);

    [RelayCommand]
    private void KeysPlus() => SetWhiteKeyCount(WhiteKeyCount + 1);

    [RelayCommand]
    private void ToggleSustain() => SustainOn = !SustainOn;

    [RelayCommand]
    private void CycleLabels() => Labels = Labels switch
    {
        PianoLabels.Off => PianoLabels.COnly,
        PianoLabels.COnly => PianoLabels.All,
        _ => PianoLabels.Off
    };

    /// <summary>Saves the settings if something changed. Cheap when nothing did.</summary>
    public void PersistIfDirty()
    {
        if (!_isDirty)
        {
            return;
        }

        _isDirty = false;
        _settings = _settings with
        {
            InstrumentId = Instrument.Id,
            PortraitWhiteKeys = _portraitCount,
            LandscapeWhiteKeys = _landscapeCount,
            PortraitFirstKey = _portraitFirst,
            LandscapeFirstKey = _landscapeFirst,
            Labels = Labels
        };
        _preferences.Save(_settings);
    }

    partial void OnSustainOnChanged(bool value) => _service.SetSustain(value);

    partial void OnLabelsChanged(PianoLabels value) => _isDirty = true;

    private void SetWhiteKeyCount(int count)
    {
        var clamped = PianoKeyboardLayout.ClampWhiteKeyCount(count);
        if (clamped == WhiteKeyCount)
        {
            return;
        }

        if (IsLandscape)
        {
            _landscapeCount = clamped;
            _landscapeFirst = PianoKeyboardLayout.ClampFirstWhiteKey(_landscapeFirst, clamped);
        }
        else
        {
            _portraitCount = clamped;
            _portraitFirst = PianoKeyboardLayout.ClampFirstWhiteKey(_portraitFirst, clamped);
        }

        _isDirty = true;
        OnPropertyChanged(nameof(WhiteKeyCount));
        OnPropertyChanged(nameof(FirstWhiteKey));
        OnPropertyChanged(nameof(KeysLabel));
        OnPropertyChanged(nameof(RangeLabel));
    }

    private void RefreshReadout()
    {
        if (_held.Count == 0)
        {
            // Keep the last notes on screen, dimmed, so they can still be read.
            ReadoutIsDim = true;
            return;
        }

        _readoutBuilder.Clear();
        for (var i = 0; i < _held.Count && i < MaxReadoutNotes; i++)
        {
            if (i > 0)
            {
                _readoutBuilder.Append(ReadoutSeparator);
            }

            _readoutBuilder.Append(NoteName(_held[i]));
        }

        ReadoutText = _readoutBuilder.ToString();
        ReadoutIsDim = false;
    }

    /// <summary>Name plus octave in scientific pitch notation, e.g. La4 or A4.</summary>
    public string NoteName(int note)
    {
        if ((uint)note >= (uint)_noteNames.Length)
        {
            return NoteNames.Get(note % 12, _resolvedNaming) + ((note / 12) - 1).ToString(CultureInfo.InvariantCulture);
        }

        // Cached per naming, so a key press builds no name string.
        return _noteNames[note] ??= NoteNames.Get(note % 12, _resolvedNaming) + ((note / 12) - 1).ToString(CultureInfo.InvariantCulture);
    }

    private NoteNaming ResolveNaming() =>
        NoteNames.Resolve(_tunerPreferences.Load().NoteNaming, CultureInfo.CurrentUICulture);

    private void OnServiceStateChanged(object? sender, EventArgs e) => _post(ApplyServiceState);

    private void ApplyServiceState()
    {
        var state = _service.State;
        IsLoading = state is PianoLoadState.NotLoaded or PianoLoadState.Loading;
        LoadFailed = state == PianoLoadState.Failed;
    }
}
