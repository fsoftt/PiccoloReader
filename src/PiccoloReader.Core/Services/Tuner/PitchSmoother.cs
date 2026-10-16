namespace PiccoloReader.Core.Services.Tuner;

/// <summary>
/// Turns raw per-frame estimates into a steady reading. A new pitch needs a few
/// consecutive consistent frames (which rejects short metronome clicks), a held
/// note ignores single outlier frames, and silence is reported only after a hold
/// time so the display does not flicker. Works in cents relative to 440 Hz.
/// </summary>
public sealed class PitchSmoother
{
    public const float ReferenceHz = 440f;
    public const float DefaultClarityGate = 0.85f;
    public const int DefaultStableFrames = 3;
    public const float DefaultHoldMs = 150f;

    private const float ToleranceCents = 50f;
    private const float EmaAlpha = 0.3f;

    // A click rings out within a few frames; a note being started does not fade that fast.
    private const float MinRmsRatioForNewNote = 0.7f;

    private readonly float _clarityGate;
    private readonly int _stableFrames;
    private readonly int _holdFrames;
    private readonly float[] _recent = new float[3];
    private int _recentCount;
    private int _recentIndex;
    private bool _locked;
    private float _lastAccepted;
    private float _ema;
    private int _misses;
    private int _candidateCount;
    private float _candidateLast;
    private float _candidateSum;
    private float _candidateFirstRms;

    public PitchSmoother(float frameDurationMs, float clarityGate = DefaultClarityGate, int stableFrames = DefaultStableFrames, float holdMs = DefaultHoldMs)
    {
        _clarityGate = clarityGate;
        _stableFrames = stableFrames;
        _holdFrames = Math.Max(1, (int)MathF.Ceiling(holdMs / frameDurationMs));
    }

    public void Reset()
    {
        _recentCount = 0;
        _recentIndex = 0;
        _locked = false;
        _misses = 0;
        _candidateCount = 0;
    }

    public TunerReading Update(in PitchEstimate estimate)
    {
        if (estimate.HasPitch && estimate.Clarity >= _clarityGate)
        {
            var cents = 1200f * MathF.Log2(estimate.Hz / ReferenceHz);
            if (_locked && MathF.Abs(cents - _lastAccepted) <= ToleranceCents)
            {
                _candidateCount = 0;
                Accept(cents, newNote: false);
            }
            else
            {
                TrackCandidate(cents, estimate.Rms);
            }
        }
        else
        {
            _candidateCount = 0;
            Miss();
        }

        if (_locked && _misses >= _holdFrames)
        {
            _locked = false;
            _recentCount = 0;
        }

        return _locked
            ? new TunerReading(true, ReferenceHz * MathF.Pow(2f, _ema / 1200f), _ema)
            : default;
    }

    private void TrackCandidate(float cents, float rms)
    {
        if (_candidateCount > 0 && MathF.Abs(cents - _candidateLast) <= ToleranceCents)
        {
            _candidateCount++;
            _candidateSum += cents;
        }
        else
        {
            _candidateCount = 1;
            _candidateSum = cents;
            _candidateFirstRms = rms;
        }

        _candidateLast = cents;
        if (_candidateCount >= _stableFrames)
        {
            if (rms < _candidateFirstRms * MinRmsRatioForNewNote)
            {
                // Decaying burst: do not lock onto it.
                _candidateCount = 0;
                Miss();
                return;
            }

            var mean = _candidateSum / _candidateCount;
            _candidateCount = 0;
            Accept(mean, newNote: true);
        }
        else
        {
            Miss();
        }
    }

    private void Miss()
    {
        if (_misses < int.MaxValue / 2)
        {
            _misses++;
        }
    }

    private void Accept(float cents, bool newNote)
    {
        _misses = 0;
        if (newNote)
        {
            _recentCount = 0;
            _recentIndex = 0;
        }

        _recent[_recentIndex] = cents;
        _recentIndex = (_recentIndex + 1) % _recent.Length;
        if (_recentCount < _recent.Length)
        {
            _recentCount++;
        }

        var median = Median();
        if (newNote || !_locked || MathF.Abs(median - _ema) > ToleranceCents)
        {
            _ema = median;
        }
        else
        {
            _ema += EmaAlpha * (median - _ema);
        }

        _locked = true;
        _lastAccepted = cents;
    }

    private float Median()
    {
        switch (_recentCount)
        {
            case 1:
                return _recent[0];
            case 2:
                return (_recent[0] + _recent[1]) * 0.5f;
            default:
                var a = _recent[0];
                var b = _recent[1];
                var c = _recent[2];
                return MathF.Max(MathF.Min(a, b), MathF.Min(MathF.Max(a, b), c));
        }
    }
}
