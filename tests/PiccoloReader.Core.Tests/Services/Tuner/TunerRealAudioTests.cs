using PiccoloReader.Core.Services.Tuner;
using Xunit.Abstractions;

namespace PiccoloReader.Core.Tests.Services.Tuner;

/// <summary>
/// Real microphone captures (Motorola, 48 kHz): a 440 Hz tone from speakers, alone and
/// with a 120 bpm metronome clicking in the same room.
/// </summary>
public class TunerRealAudioTests(ITestOutputHelper output)
{
    private const double SkipMs = 1000;

    private (double PerSecond, double Min, double Max, double Median, int Count, bool LostPitch) Measure(string name)
    {
        var (samples, rate) = WavReader.Read(WavReader.Fixture(name));
        var results = new List<(double TimeMs, TunerReading Reading)>();
        var position = 0;
        var engine = new TunerEngine(rate, r => results.Add((position * 1000.0 / rate, r)));
        for (position = 0; position < samples.Length; position += 2048)
        {
            engine.ProcessBlock(samples.AsSpan(position, Math.Min(2048, samples.Length - position)));
        }

        var after = results.Where(r => r.TimeMs >= SkipMs).ToList();
        var cents = after.Where(r => r.Reading.HasPitch).Select(r => (double)r.Reading.CentsFromA4).OrderBy(c => c).ToList();
        var seconds = (samples.Length * 1000.0 / rate - SkipMs) / 1000.0;
        var lost = after.Any(r => !r.Reading.HasPitch);
        var m = (after.Count / seconds, cents.Count == 0 ? 0 : cents[0], cents.Count == 0 ? 0 : cents[^1], cents.Count == 0 ? 0 : cents[cents.Count / 2], after.Count, lost);

        output.WriteLine($"{name}: {m.Item1:F2}/s emissions={after.Count} cents[{m.Item2:F2},{m.Item3:F2}] lost={lost}");
        return m;
    }

    [Fact]
    public void ToneOnly_IsSteady()
    {
        var a = Measure("tuner_tone440.wav");
        Assert.False(a.LostPitch);
        Assert.True(a.PerSecond <= 1.0, $"{a.PerSecond}/s");
        Assert.InRange(a.Median, -5, 5);
    }

    [Fact]
    public void ToneWithMetronome_StaysSteadyAndQuiet()
    {
        var a = Measure("tuner_tone440.wav");
        var b = Measure("tuner_tone440_metronome120.wav");
        Assert.False(b.LostPitch);
        Assert.True(b.PerSecond <= 1.0, $"{b.PerSecond}/s");
        Assert.InRange(b.Min, a.Median - 1.5, a.Median + 1.5);
        Assert.InRange(b.Max, a.Median - 1.5, a.Median + 1.5);
    }
}
