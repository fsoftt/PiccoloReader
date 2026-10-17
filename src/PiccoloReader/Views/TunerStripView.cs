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
    internal ITunerStripView? Surface
    {
        get => _surface;
        set
        {
            _surface = value;
            // The handler can connect after the controller already switched the strip on.
            _surface?.SetActive(_active);
        }
    }

    private ITunerStripView? _surface;
    private bool _active;
    private bool _vertical;

    /// <summary>True when the strip sits on the left or right edge (marker moves along Y).</summary>
    internal bool IsVertical
    {
        get => _vertical;
        set
        {
            _vertical = value;
            VerticalChanged?.Invoke(value);
        }
    }

    /// <summary>Set by the platform handler while connected.</summary>
    internal Action<bool>? VerticalChanged { get; set; }

    /// <summary>Raised by the handler when the track width changes; argument is the marker travel in pixels.</summary>
    internal Action<float>? HalfTrackChanged { get; set; }

    public void SetActive(bool active)
    {
        _active = active;
        _surface?.SetActive(active);
    }

    public void Apply(bool visible, int fillPx, TunerStripBucket bucket) => Surface?.Apply(visible, fillPx, bucket);
}
