namespace PiccoloReader.Core.Services.Metronome;

/// <summary>
/// Sample-exact click placement. Each <see cref="Fill"/> renders the next
/// chunk of the stream; beats are placed by absolute frame index, so tempo
/// never drifts. Allocation free after construction. Not thread safe: use it
/// from the audio thread only.
/// </summary>
public sealed class MetronomeScheduler
{
    public const int MaxBeatEvents = 8;

    private readonly long _framesPerMinute;
    private readonly ClickSet _clicks;

    private long _position;
    private long _nextTickFrame;
    private bool _beatPending = true;
    private int _beatInBar = -1;
    private long _beatStart;
    private long _beatEnd;
    private bool _anchored;
    private long _anchorFrame;
    private int _anchorBpm;
    private long _beatsSinceAnchor;
    private int _subIndex;
    private int _subCount;
    private bool _accentOn;
    private short[]? _click;
    private int _clickPos;

    public MetronomeScheduler(int sampleRate, ClickSet clicks)
    {
        _framesPerMinute = (long)sampleRate * 60;
        _clicks = clicks;
    }

    /// <summary>Frames rendered so far (absolute index of the next chunk's first frame).</summary>
    public long Position => _position;

    /// <summary>Main beats whose click starts inside the last filled chunk.</summary>
    public BeatEvent[] BeatEvents { get; } = new BeatEvent[MaxBeatEvents];

    public int BeatEventCount { get; private set; }

    /// <summary>
    /// Renders <paramref name="frames"/> mono frames into the buffer. Tempo,
    /// bar length, accent and subdivision changes apply from the next beat.
    /// </summary>
    public void Fill(short[] buffer, int frames, MetronomeSettings settings)
    {
        Array.Clear(buffer, 0, frames);
        BeatEventCount = 0;

        var end = _position + frames;
        var cursor = 0;
        while (_nextTickFrame < end)
        {
            var offset = (int)(_nextTickFrame - _position);
            CopyActiveClick(buffer, cursor, offset);
            cursor = offset;
            StartTick(settings);
        }

        CopyActiveClick(buffer, cursor, frames);
        _position = end;
    }

    private void CopyActiveClick(short[] buffer, int from, int to)
    {
        if (_click is null || to <= from)
        {
            return;
        }

        var count = Math.Min(to - from, _click.Length - _clickPos);
        Array.Copy(_click, _clickPos, buffer, from, count);
        _clickPos += count;
        if (_clickPos >= _click.Length)
        {
            _click = null;
        }
    }

    private void StartTick(MetronomeSettings settings)
    {
        if (_beatPending)
        {
            BeginBeat(settings);
        }

        var isMain = _subIndex == 0;
        var isAccent = isMain && _accentOn && _beatInBar == 0;
        _click = isMain ? (isAccent ? _clicks.Accent : _clicks.Normal) : _clicks.Subdivision;
        _clickPos = 0;

        if (isMain && BeatEventCount < MaxBeatEvents)
        {
            BeatEvents[BeatEventCount++] = new BeatEvent(_nextTickFrame, _beatInBar, isAccent);
        }

        _subIndex++;
        if (_subIndex < _subCount)
        {
            _nextTickFrame = _beatStart + _subIndex * (_beatEnd - _beatStart) / _subCount;
        }
        else
        {
            _beatPending = true;
            _nextTickFrame = _beatEnd;
        }
    }

    private void BeginBeat(MetronomeSettings settings)
    {
        _beatStart = _nextTickFrame;
        _beatInBar = (_beatInBar + 1) % settings.BeatsPerBar;

        // Beats come from an anchor with exact integer math, so rounding
        // never accumulates; a tempo change re-anchors at this beat.
        if (!_anchored || settings.Bpm != _anchorBpm)
        {
            _anchored = true;
            _anchorFrame = _beatStart;
            _anchorBpm = settings.Bpm;
            _beatsSinceAnchor = 0;
        }
        else
        {
            _beatsSinceAnchor++;
        }

        _beatEnd = _anchorFrame + (_beatsSinceAnchor + 1) * _framesPerMinute / _anchorBpm;
        _subCount = settings.Subdivision;
        _subIndex = 0;
        _accentOn = settings.AccentFirstBeat;
        _beatPending = false;
    }
}

/// <summary>A main beat whose click starts at <see cref="Frame"/> (absolute stream index).</summary>
public readonly record struct BeatEvent(long Frame, int BeatInBar, bool IsAccent);
