using PiccoloReader.Core.Services.Tuner;

namespace PiccoloReader.Views;

/// <summary>
/// Placeholder MAUI view for the tuner strip. All drawing lives in the native view
/// of its handler (Android only); this class just forwards updates to it, so
/// readings never touch MAUI properties or layout.
/// </summary>
public class TunerStripView : View, ITunerStripView
{
    /// <summary>Set by the platform handler while connected.</summary>
    internal ITunerStripView? Surface { get; set; }

    /// <summary>Raised by the handler when the track width changes; argument is the marker travel in pixels.</summary>
    internal Action<float>? TravelChanged { get; set; }

    public void Apply(bool visible, int offsetPx, TunerStripBucket bucket) => Surface?.Apply(visible, offsetPx, bucket);
}
