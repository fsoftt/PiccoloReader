using Xunit.Abstractions;

namespace PiccoloReader.Core.Tests.Services.Tuner;

public class TunerClickEmissionTests(ITestOutputHelper output)
{
    private const double Seconds = 10;
    private const double SkipMs = 1000;

    private (double PerSecond, double MaxCentsError) Measure(bool clicks)
    {
        var signal = TunerTestSignals.Sine(440, Seconds, 0.1);
        var noise = TunerTestSignals.Noise(Seconds, 0.002);
        for (var i = 0; i < signal.Length; i++)
        {
            signal[i] += noise[i];
        }

        if (clicks)
        {
            TunerTestSignals.MixClicks(signal, 500, 0.7);
        }

        var readings = TunerTestSignals.Run(signal).Where(r => r.TimeMs >= SkipMs).ToList();
        var maxError = readings.Count == 0
            ? 0
            : readings.Max(r => r.Reading.HasPitch ? Math.Abs(r.Reading.CentsFromA4) : 1000);
        return (readings.Count / ((Seconds * 1000 - SkipMs) / 1000), maxError);
    }

    [Fact]
    public void ClicksOverSteadyTone_DoNotCauseExtraEmissions()
    {
        var without = Measure(false);
        var with = Measure(true);
        output.WriteLine($"no clicks: {without.PerSecond:F2}/s maxErr {without.MaxCentsError:F2} cents");
        output.WriteLine($"clicks:    {with.PerSecond:F2}/s maxErr {with.MaxCentsError:F2} cents");

        Assert.True(with.MaxCentsError <= 1.0, $"max error {with.MaxCentsError}");
        Assert.True(with.PerSecond <= Math.Max(without.PerSecond * 1.5, 1.0), $"{with.PerSecond}/s vs {without.PerSecond}/s");
    }
}
