namespace PiccoloReader.Core.Services.Metronome;

/// <summary>
/// Immutable metronome configuration. Every property clamps to its valid
/// range, so a snapshot can be handed to the audio thread without checks.
/// </summary>
public sealed record MetronomeSettings
{
    public const int MinBpm = 30;
    public const int MaxBpm = 250;
    public const int MinBeatsPerBar = 1;
    public const int MaxBeatsPerBar = 12;
    public const int MinSubdivision = 1;
    public const int MaxSubdivision = 4;

    private readonly int _bpm = 100;
    private readonly int _beatsPerBar = 4;
    private readonly int _subdivision = 1;

    public int Bpm
    {
        get => _bpm;
        init => _bpm = Math.Clamp(value, MinBpm, MaxBpm);
    }

    public int BeatsPerBar
    {
        get => _beatsPerBar;
        init => _beatsPerBar = Math.Clamp(value, MinBeatsPerBar, MaxBeatsPerBar);
    }

    public bool AccentFirstBeat { get; init; } = true;

    /// <summary>Clicks per beat: 1 = none, 2 = eighths, 3 = triplets, 4 = sixteenths.</summary>
    public int Subdivision
    {
        get => _subdivision;
        init => _subdivision = Math.Clamp(value, MinSubdivision, MaxSubdivision);
    }
}
