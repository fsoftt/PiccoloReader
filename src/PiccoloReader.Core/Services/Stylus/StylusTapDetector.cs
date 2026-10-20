namespace PiccoloReader.Core.Services.Stylus;

public static class StylusTapDetector
{
    /// <summary>Longest touch that still counts as a tap (same as the finger path).</summary>
    public const long TapMaxDurationMs = 300;

    /// <summary>
    /// A stylus gesture is a tap, not a stroke, when it neither travelled
    /// farther than the touch slop nor lasted TapMaxDurationMs or more.
    /// </summary>
    public static bool IsTap(double maxTravelPx, long durationMs, double slopPx) =>
        maxTravelPx < slopPx && durationMs < TapMaxDurationMs;
}
