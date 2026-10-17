namespace PiccoloReader.Core.Services.Tuner;

public interface ITunerStripPreferenceService
{
    /// <summary>Global (not per sheet) switch of the tuner strip in the sheet viewer. Off on first install.</summary>
    bool IsEnabled { get; set; }

    /// <summary>Edge of the viewer where the strip is drawn. Right by default.</summary>
    TunerStripPosition Position { get; set; }
}
