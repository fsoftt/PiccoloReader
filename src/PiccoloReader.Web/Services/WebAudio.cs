using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PiccoloReader.Core.Services.Metronome;
using PiccoloReader.Core.Services.Piano;
using PiccoloReader.Core.Services.Tuner;

namespace PiccoloReader.Web.Services;

/// <summary>
/// Bridge to wwwroot/js/audio.js: one AudioContext, output streams played by an AudioWorklet
/// and the microphone capture. Nothing is created until the first tool asks for audio, so the
/// app start is not slowed down by it.
/// </summary>
public sealed class WebAudio(IJSRuntime js)
{
    private readonly Dictionary<string, Action<long, long>> _reports = new();
    private IJSInProcessObjectReference? _module;
    private Task<bool>? _ready;
    private Action<byte[]>? _micBlock;

    public bool IsReady => _module is not null && _ready is { IsCompletedSuccessfully: true };

    public int SampleRate => _module?.Invoke<int>("sampleRate") ?? 48000;

    public double LatencySeconds => _module?.Invoke<double>("latencySeconds") ?? 0;

    /// <summary>AudioContext state (suspended, running, ...), "none" before the first use.</summary>
    public string ContextState => _module?.Invoke<string>("state") ?? "none";

    /// <summary>Loads the module, creates the context and the worklets. False when the browser cannot do it.</summary>
    public Task<bool> EnsureReadyAsync() => _ready ??= InitAsync();

    private async Task<bool> InitAsync()
    {
        try
        {
            _module = (IJSInProcessObjectReference)await js.InvokeAsync<IJSObjectReference>("import", "./js/audio.js");
            await _module.InvokeAsync<bool>("init", DotNetObjectReference.Create(this));
            return true;
        }
        catch (Exception)
        {
            _ready = null;
            return false;
        }
    }

    public void Resume() => _module?.InvokeVoid("resume");

    internal void StreamStart(string name, int channels, Action<long, long> onReport)
    {
        _reports[name] = onReport;
        _module!.InvokeVoid("streamStart", name, channels);
    }

    internal void StreamPush(string name, byte[] data, int frames) => _module!.InvokeVoid("streamPush", name, data, frames);

    internal void StreamStop(string name)
    {
        _reports.Remove(name);
        _module?.InvokeVoid("streamStop", name);
    }

    [JSInvokable]
    public void OnStreamReport(string name, double played, double queued)
    {
        if (_reports.TryGetValue(name, out var handler))
        {
            handler((long)played, (long)queued);
        }
    }

    [JSInvokable]
    public void OnMicBlock(byte[] data) => _micBlock?.Invoke(data);

    // Sound font ------------------------------------------------------------------------------

    public async Task<byte[]> FetchCachedAsync(string url)
    {
        await EnsureReadyAsync();
        return await _module!.InvokeAsync<byte[]>("fetchCached", url);
    }

    // Microphone ------------------------------------------------------------------------------

    public string MicPermission => _module?.Invoke<string>("micPermission") ?? "unknown";

    public string LastMicResult { get; private set; } = "unknown";

    public async Task RefreshMicPermissionAsync()
    {
        if (await EnsureReadyAsync())
        {
            await _module!.InvokeAsync<string>("micRefresh");
        }
    }

    public async Task<string> MicRequestAsync()
    {
        if (!await EnsureReadyAsync())
        {
            return LastMicResult = "unsupported";
        }

        return LastMicResult = await _module!.InvokeAsync<string>("micRequest");
    }

    internal int MicStart(Action<byte[]> onBlock)
    {
        _micBlock = onBlock;
        return _module?.Invoke<int>("micStart") ?? 0;
    }

    internal void MicStop()
    {
        _micBlock = null;
        _module?.InvokeVoid("micStop", true);
    }

    internal bool MicActive => _module?.Invoke<bool>("micActive") ?? false;

    // Keyboard --------------------------------------------------------------------------------

    public void PianoAttach<T>(ElementReference root, DotNetObjectReference<T> bridge) where T : class =>
        _module?.InvokeVoid("pianoAttach", root, bridge);

    public void PianoRelease(ElementReference root) => _module?.InvokeVoid("pianoRelease", root);

    public void PianoDetach(ElementReference root) => _module?.InvokeVoid("pianoDetach", root);

    public void WatchOrientation<T>(DotNetObjectReference<T> bridge) where T : class =>
        _module?.InvokeVoid("watchOrientation", bridge);

    public void UnwatchOrientation() => _module?.InvokeVoid("unwatchOrientation");

    public void MinimapAttach<T>(ElementReference root, DotNetObjectReference<T> bridge) where T : class =>
        _module?.InvokeVoid("minimapAttach", root, bridge);
}

/// <summary>
/// Piano output: the worklet reports how much it still holds; every report tops the queue up
/// to <see cref="TargetFrames"/> with small blocks rendered by the synth on the spot, so a key
/// press is heard after at most the queued audio plus the browser's own output latency.
/// </summary>
public sealed class WebPianoOutput(WebAudio audio) : IPianoAudioOutput
{
    public const int BlockFrames = 256;
    public const int TargetFrames = 1536;
    private const int MaxBlocksPerReport = 6;
    private const string Stream = "piano";

    private readonly float[] _left = new float[BlockFrames];
    private readonly float[] _right = new float[BlockFrames];
    private readonly float[] _planar = new float[BlockFrames * 2];
    private readonly byte[] _bytes = new byte[BlockFrames * 2 * sizeof(float)];
    private PianoSynthEngine? _engine;

    /// <summary>Worst time spent rendering one block since the output started (milliseconds), for latency diagnostics.</summary>
    public double WorstBlockMs { get; private set; }

    public double LastReportMs { get; private set; }

    public int PreferredSampleRate => audio.SampleRate;

    public bool IsRunning { get; private set; }

    public event EventHandler? Died
    {
        add { }
        remove { }
    }

    public bool Start(PianoSynthEngine engine)
    {
        if (!audio.IsReady)
        {
            return false;
        }

        Stop();
        _engine = engine;
        WorstBlockMs = 0;
        IsRunning = true;
        audio.StreamStart(Stream, 2, OnReport);
        return true;
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;
        _engine = null;
        audio.StreamStop(Stream);
    }

    private void OnReport(long played, long queued)
    {
        var engine = _engine;
        if (engine is null)
        {
            return;
        }

        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < MaxBlocksPerReport && queued < TargetFrames; i++)
        {
            var block = Stopwatch.GetTimestamp();
            engine.Render(_left, _right);
            _left.CopyTo(_planar, 0);
            _right.CopyTo(_planar, BlockFrames);
            Buffer.BlockCopy(_planar, 0, _bytes, 0, _bytes.Length);
            audio.StreamPush(Stream, _bytes, BlockFrames);
            queued += BlockFrames;
            WorstBlockMs = Math.Max(WorstBlockMs, Stopwatch.GetElapsedTime(block).TotalMilliseconds);
        }

        LastReportMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }
}

/// <summary>
/// Metronome output: <see cref="MetronomeScheduler"/> places the clicks by absolute frame in a
/// mono stream, so the tempo is sample exact whatever the timers do. Beat lamps are fired when
/// the worklet reports it has played up to the click (plus the output latency).
/// </summary>
public sealed class WebMetronomePlayer(WebAudio audio) : IMetronomePlayer
{
    private const int BlockFrames = 512;
    private const int TargetFrames = 2048;
    private const string Stream = "metronome";

    private readonly short[] _pcm = new short[BlockFrames];
    private readonly float[] _mono = new float[BlockFrames];
    private readonly byte[] _bytes = new byte[BlockFrames * sizeof(float)];
    private readonly Queue<BeatEvent> _beats = new();
    private MetronomeScheduler? _scheduler;
    private ClickSet? _clicks;
    private int _clickRate;
    private MetronomeSettings _settings = new();
    private int _version;

    public bool IsPlaying { get; private set; }

    public event Action<int, bool>? BeatTicked;

    public event EventHandler? IsPlayingChanged;

    public void UpdateSettings(MetronomeSettings settings) => _settings = settings;

    public void Start()
    {
        if (IsPlaying)
        {
            return;
        }

        IsPlaying = true;
        IsPlayingChanged?.Invoke(this, EventArgs.Empty);
        _ = BeginAsync(++_version);
    }

    public void Stop()
    {
        if (!IsPlaying)
        {
            return;
        }

        _version++;
        IsPlaying = false;
        _beats.Clear();
        _scheduler = null;
        audio.StreamStop(Stream);
        IsPlayingChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task BeginAsync(int version)
    {
        var ok = await audio.EnsureReadyAsync();
        if (version != _version)
        {
            return;
        }

        if (!ok)
        {
            Stop();
            return;
        }

        var rate = audio.SampleRate;
        if (_clicks is null || _clickRate != rate)
        {
            _clicks = new ClickSet(rate);
            _clickRate = rate;
        }

        _scheduler = new MetronomeScheduler(rate, _clicks);
        _beats.Clear();
        audio.StreamStart(Stream, 1, OnReport);
    }

    private void OnReport(long played, long queued)
    {
        var scheduler = _scheduler;
        if (scheduler is null || !IsPlaying)
        {
            return;
        }

        var heard = played + (long)(audio.LatencySeconds * audio.SampleRate);
        while (_beats.Count > 0 && _beats.Peek().Frame <= heard)
        {
            var beat = _beats.Dequeue();
            BeatTicked?.Invoke(beat.BeatInBar, beat.IsAccent);
        }

        while (queued < TargetFrames)
        {
            scheduler.Fill(_pcm, BlockFrames, _settings);
            for (var i = 0; i < scheduler.BeatEventCount; i++)
            {
                _beats.Enqueue(scheduler.BeatEvents[i]);
            }

            for (var i = 0; i < BlockFrames; i++)
            {
                _mono[i] = _pcm[i] / 32768f;
            }

            Buffer.BlockCopy(_mono, 0, _bytes, 0, _bytes.Length);
            audio.StreamPush(Stream, _bytes, BlockFrames);
            queued += BlockFrames;
        }
    }
}

/// <summary>Microphone through getUserMedia and the capture worklet, feeding the shared <see cref="TunerEngine"/>.</summary>
public sealed class WebTunerInput(WebAudio audio) : ITunerInput
{
    private TunerEngine? _engine;
    private bool _running;

    public bool IsRunning => _running && audio.MicActive;

    public event EventHandler<TunerReading>? ReadingChanged;

    // The permission service already opened the stream (the only async part); this just wires it up.
    public TunerStartResult Start()
    {
        if (audio.LastMicResult == "unsupported")
        {
            return TunerStartResult.Unsupported;
        }

        if (audio.LastMicResult == "denied")
        {
            return TunerStartResult.PermissionDenied;
        }

        var rate = audio.MicStart(OnBlock);
        if (rate < 32000)
        {
            audio.MicStop();
            return TunerStartResult.Failed;
        }

        _engine = new TunerEngine(rate, reading => ReadingChanged?.Invoke(this, reading));
        _running = true;
        return TunerStartResult.Started;
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        audio.MicStop();
        _engine = null;
    }

    private void OnBlock(byte[] data)
    {
        if (_engine is { } engine)
        {
            engine.ProcessBlock(MemoryMarshal.Cast<byte, float>(data));
        }
    }
}

public sealed class WebMicrophonePermission(WebAudio audio) : IMicrophonePermissionService
{
    // "error" (no microphone) and "unsupported" count as handled: Start then reports why.
    public async Task<bool> EnsureGrantedAsync() => await audio.MicRequestAsync() != "denied";

    public bool IsGranted() => audio.MicPermission == "granted";

    // A browser page cannot open the site settings; the tuner page offers a retry instead.
    public void OpenAppSettings() { }
}
