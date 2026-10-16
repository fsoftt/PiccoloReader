namespace PiccoloReader.Core.Services.Metronome;

/// <summary>Estimates BPM from tap timestamps (milliseconds, any monotonic clock).</summary>
public sealed class TapTempo
{
    public const int MaxIntervals = 4;
    public const long ResetGapMs = 2000;

    private readonly long[] _taps = new long[MaxIntervals + 1];
    private int _count;

    /// <summary>Registers a tap. Returns the BPM averaged over the last intervals, or null on the first tap of a series.</summary>
    public int? Tap(long timestampMs)
    {
        if (_count > 0 && timestampMs - _taps[_count - 1] > ResetGapMs)
        {
            _count = 0;
        }

        if (_count == _taps.Length)
        {
            Array.Copy(_taps, 1, _taps, 0, _taps.Length - 1);
            _count--;
        }

        _taps[_count++] = timestampMs;
        if (_count < 2)
        {
            return null;
        }

        var averageMs = (double)(_taps[_count - 1] - _taps[0]) / (_count - 1);
        if (averageMs <= 0)
        {
            return null;
        }

        var bpm = (int)Math.Round(60000.0 / averageMs);
        return Math.Clamp(bpm, MetronomeSettings.MinBpm, MetronomeSettings.MaxBpm);
    }

    public void Reset() => _count = 0;
}
