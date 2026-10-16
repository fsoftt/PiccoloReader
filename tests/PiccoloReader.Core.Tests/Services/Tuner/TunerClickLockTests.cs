using PiccoloReader.Core.Services.Tuner;
using Xunit.Abstractions;

namespace PiccoloReader.Core.Tests.Services.Tuner;

public class TunerClickLockTests(ITestOutputHelper output)
{
    private sealed record Timeline(double FirstLockMs, double DroppedMs, double WrongMs, int Drops, double TotalMs);

    private static float[] ToneWithClicks(double hz, double toneAmp, double clickAmp, double clickMs, double seconds, double clickOffsetMs, double noise = 0.003)
    {
        var s = TunerTestSignals.Sine(hz, seconds, toneAmp);
        var random = new Random(1);
        for (var i = 0; i < s.Length; i++)
        {
            s[i] += (float)((random.NextDouble() - 0.5) * 2 * noise);
        }

        if (clickAmp > 0)
        {
            var clicks = new float[s.Length];
            TunerTestSignals.MixClicks(clicks, 500, clickAmp, clickMs);
            var shift = (int)(clickOffsetMs * TunerTestSignals.SampleRate / 1000);
            for (var i = 0; i + shift < s.Length; i++)
            {
                s[i + shift] += clicks[i];
            }
        }

        return s;
    }

    private static Timeline Analyse(float[] signal, double hz)
    {
        var readings = TunerTestSignals.Run(signal);
        var totalMs = signal.Length * 1000.0 / TunerTestSignals.SampleRate;
        var first = readings.FirstOrDefault(r => r.Reading.HasPitch && Math.Abs(TunerTestSignals.CentsBetween(r.Reading.Hz, hz)) < 20);
        if (first.Reading.HasPitch is false)
        {
            return new Timeline(-1, totalMs, 0, 0, totalMs);
        }

        double dropped = 0, wrong = 0;
        var drops = 0;
        var state = 0; // 0 right, 1 none, 2 wrong
        var since = first.TimeMs;
        foreach (var (t, r) in readings.Where(r => r.TimeMs > first.TimeMs))
        {
            var next = !r.HasPitch ? 1 : Math.Abs(TunerTestSignals.CentsBetween(r.Hz, hz)) < 20 ? 0 : 2;
            Close(state, t - since);
            if (next == 1 && state != 1)
            {
                drops++;
            }

            state = next;
            since = t;
        }

        Close(state, totalMs - since);
        return new Timeline(first.TimeMs, dropped, wrong, drops, totalMs);

        void Close(int s, double ms)
        {
            if (s == 1) dropped += ms;
            else if (s == 2) wrong += ms;
        }
    }

    [Theory]
    [InlineData(25, 0.2)]
    [InlineData(25, 0.8)]
    [InlineData(80, 0.6)]
    [InlineData(120, 0.9)]
    [InlineData(200, 0.9)]
    public void Tone440_WithLoudClicksAt120Bpm_LocksFastAndStaysDisplayed(double clickMs, double clickAmp)
    {
        var signal = ToneWithClicks(440, 0.02, clickAmp, clickMs, 6, 0);

        var t = Analyse(signal, 440);
        output.WriteLine($"CLICK dur{clickMs} amp{clickAmp}: lock={t.FirstLockMs:F0}ms dropped={t.DroppedMs:F0}ms in {t.Drops} drops, wrong={t.WrongMs:F0}ms");

        Assert.InRange(t.FirstLockMs, 0, 300);
        Assert.Equal(0, t.Drops);
        Assert.Equal(0, t.WrongMs);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.6)]
    public void Tone2093_LocksWithin300Ms(double clickAmp)
    {
        var signal = ToneWithClicks(2093, 0.02, clickAmp, 25, 4, 0, 0.005);

        var t = Analyse(signal, 2093);
        output.WriteLine($"HIGH click{clickAmp}: lock={t.FirstLockMs:F0}ms dropped={t.DroppedMs:F0}ms in {t.Drops} drops, wrong={t.WrongMs:F0}ms");

        Assert.InRange(t.FirstLockMs, 0, 300);
        Assert.Equal(0, t.Drops);
        Assert.Equal(0, t.WrongMs);
    }
}
