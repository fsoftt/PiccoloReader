namespace PiccoloReader.Core.Services.Tuner;

/// <summary>
/// Spots analysis windows that a short loud event (a metronome click, a tap)
/// has corrupted, by comparing the window RMS with a quickly decaying running
/// level of the sound before it. Such windows are skipped instead of analysed,
/// so the click neither replaces nor resets the note being shown. A level
/// that stays high for more than <see cref="MaxSkippedFrames"/> frames is not a
/// transient, and becomes the new running level.
/// </summary>
internal sealed class TransientGate
{
    public const float JumpFactor = 2.5f;
    public const int MaxSkippedFrames = 8;
    private const float MinLevel = 0.003f;
    private const float FallAlpha = 0.4f;
    private const float RiseAlpha = 0.3f;

    private float _level;
    private int _run;

    public void Reset()
    {
        _level = 0f;
        _run = 0;
    }

    public bool IsTransient(float rms)
    {
        if (_level < MinLevel)
        {
            _level = rms;
            _run = 0;
            return false;
        }

        if (rms <= _level * JumpFactor)
        {
            _level += (rms - _level) * (rms < _level ? FallAlpha : RiseAlpha);
            _run = 0;
            return false;
        }

        if (++_run > MaxSkippedFrames)
        {
            _level = rms;
            _run = 0;
            return false;
        }

        return true;
    }
}
