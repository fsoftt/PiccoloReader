namespace PiccoloReader.Core.Services.Piano;

public enum PianoLabels
{
    Off,
    COnly,
    All
}

/// <summary>Persisted piano state. Sustain is deliberately absent: it always starts off.</summary>
public sealed record PianoSettings
{
    public const int DefaultPortraitFirstKey = 60;
    public const int DefaultLandscapeFirstKey = 48;

    public string InstrumentId { get; init; } = PianoInstruments.Default.Id;

    /// <summary>White keys shown in portrait (C4 to E5 by default).</summary>
    public int PortraitWhiteKeys { get; init; } = 10;

    /// <summary>White keys shown in landscape (C3 to E5 by default).</summary>
    public int LandscapeWhiteKeys { get; init; } = 17;

    public int PortraitFirstKey { get; init; } = DefaultPortraitFirstKey;

    public int LandscapeFirstKey { get; init; } = DefaultLandscapeFirstKey;

    public PianoLabels Labels { get; init; } = PianoLabels.COnly;

    /// <summary>White keys of the sheet viewer strip (phase 2).</summary>
    public int StripWhiteKeys { get; init; } = 14;

    public int StripFirstKey { get; init; } = DefaultPortraitFirstKey;
}

public interface IPianoPreferenceService
{
    PianoSettings Load();

    void Save(PianoSettings settings);
}
