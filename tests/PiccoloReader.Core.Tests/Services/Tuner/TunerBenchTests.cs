using System.Diagnostics;
using PiccoloReader.Core.Services.Tuner;
using Xunit.Abstractions;

namespace PiccoloReader.Core.Tests.Services.Tuner;

/// <summary>
/// CPU cost of the tuner pipeline. Wall-clock numbers are printed for reference
/// (run with -c Release); the assertions use the number of lags the detector
/// evaluates, which is deterministic and what dominates the cost on a slow JIT.
/// </summary>
public class TunerBenchTests(ITestOutputHelper output)
{
    private const double Seconds = 10;

    private (double MicrosPerSecond, double LagsPerSecond) Measure(string name, float[] signal)
    {
        var engine = new TunerEngine(TunerTestSignals.SampleRate, _ => { });
        const int chunk = 1024;
        for (var warm = 0; warm < 3; warm++)
        {
            for (var p = 0; p + chunk <= signal.Length; p += chunk)
            {
                engine.ProcessBlock(signal.AsSpan(p, chunk));
            }
        }

        var lagsBefore = engine.LagEvaluations;
        var sw = Stopwatch.StartNew();
        for (var p = 0; p + chunk <= signal.Length; p += chunk)
        {
            engine.ProcessBlock(signal.AsSpan(p, chunk));
        }

        sw.Stop();
        var micros = sw.Elapsed.TotalMilliseconds * 1000 / Seconds;
        var lags = (engine.LagEvaluations - lagsBefore) / Seconds;
        output.WriteLine($"BENCH {name}: {micros:F0} us and {lags:F0} lags per second of audio ({micros / 10000:F3} % of one core)");
        return (micros, lags);
    }

    [Fact]
    public void SteadyTone_CostsAFewLagsPerHop()
    {
        // Tracking verifies 5 lags per hop (47 hops/s) plus the occasional sub-multiple check.
        Assert.True(Measure("440 Hz", TunerTestSignals.Sine(440, Seconds)).LagsPerSecond < 750);
        Assert.True(Measure("2093 Hz", TunerTestSignals.Sine(2093, Seconds)).LagsPerSecond < 750);
        Assert.True(Measure("harmonic 440 Hz", TunerTestSignals.Harmonic(440, Seconds)).LagsPerSecond < 750);
    }

    [Fact]
    public void Silence_EvaluatesNoLags()
    {
        Assert.Equal(0, Measure("silence", TunerTestSignals.Silence(Seconds)).LagsPerSecond);
    }

    [Fact]
    public void Noise_StaysBounded()
    {
        // Worst case: no pitch to track, every searched hop scans the whole lag range (about 56 lags).
        Assert.True(Measure("white noise", TunerTestSignals.Noise(Seconds)).LagsPerSecond < 3200);
    }

    [Fact]
    public void TonePlusClicks_StaysCheap()
    {
        var signal = TunerTestSignals.Sine(440, Seconds, 0.02);
        TunerTestSignals.MixClicks(signal, 500, 0.6);
        Assert.True(Measure("440 Hz + clicks", signal).LagsPerSecond < 1000);
    }
}
