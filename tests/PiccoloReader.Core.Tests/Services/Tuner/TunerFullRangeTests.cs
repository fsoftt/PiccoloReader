using PiccoloReader.Core.Services.Tuner;
using Xunit.Abstractions;

namespace PiccoloReader.Core.Tests.Services.Tuner;

public class TunerFullRangeTests(ITestOutputHelper output)
{
    private const int Rate = TunerTestSignals.SampleRate;
    private const double RolledOffAbove = 1000;
    private const double VeryHigh = 3000;

    public static TheoryData<double> Frequencies() =>
        [29.14, 41.2, 58.27, 110, 220, 349.2, 392, 440, 466.2, 880, 2093, 4186];

    private static float[] Tone(double hz, double seconds, bool harmonicRich)
    {
        var data = new float[(int)(Rate * seconds)];
        for (var i = 0; i < data.Length; i++)
        {
            double v;
            if (harmonicRich && hz > RolledOffAbove && hz < VeryHigh)
            {
                // Above 1 kHz a real tone has little left above its 2nd harmonic, and a
                // flat spectrum with the partials beyond 8 kHz cannot be told from a note
                // an octave up at a 24 kHz analysis rate.
                v = 0;
                for (var h = 1; h <= 4; h++)
                {
                    v += Math.Sin(2 * Math.PI * hz * h * i / Rate) / h;
                }

                v *= 0.25;
            }
            else if (harmonicRich && hz >= VeryHigh)
            {
                // The top octave: a faint 2nd harmonic only.
                v = 0.3 * (Math.Sin(2 * Math.PI * hz * i / Rate) + 0.1 * Math.Sin(4 * Math.PI * hz * i / Rate));
            }
            else if (harmonicRich)
            {
                v = 0.3 * Math.Sin(2 * Math.PI * hz * i / Rate);
                for (var h = 2; h <= 6; h++)
                {
                    if (hz * h < Rate / 2.0)
                    {
                        v += Math.Sin(2 * Math.PI * hz * h * i / Rate);
                    }
                }

                v *= 0.12;
            }
            else
            {
                v = 0.3 * Math.Sin(2 * Math.PI * hz * i / Rate);
            }

            data[i] = (float)v;
        }

        return data;
    }

    // The engine only emits when the reading changes, so the reading at a time is the last one emitted before it.
    private static TunerReading? At(List<(double TimeMs, TunerReading Reading)> readings, double timeMs)
    {
        TunerReading? result = null;
        foreach (var (time, reading) in readings)
        {
            if (time <= timeMs)
            {
                result = reading;
            }
        }

        return result;
    }

    private void AssertSettled(List<(double TimeMs, TunerReading Reading)> readings, double hz, double totalMs)
    {
        // Every reading emitted after the first 500 ms, plus the one standing at the end.
        var checks = readings.Where(r => r.TimeMs > 500).Select(r => r.Reading).ToList();
        checks.Add(At(readings, totalMs) ?? default);
        foreach (var reading in checks)
        {
            Assert.True(reading.HasPitch, $"{hz} Hz: no pitch");
            var cents = TunerTestSignals.CentsBetween(reading.Hz, hz);
            Assert.True(Math.Abs(cents) <= 3.0, $"{hz} Hz read {reading.Hz} Hz ({cents:F1} cents)");
        }

        output.WriteLine($"{hz} Hz -> {checks[^1].Hz:F2} Hz");
    }

    [Theory]
    [MemberData(nameof(Frequencies))]
    public void Sine_IsWithinThreeCents(double hz)
    {
        var readings = TunerTestSignals.Run(Tone(hz, 2.0, false));

        AssertSettled(readings, hz, 2000);
    }

    [Theory]
    [MemberData(nameof(Frequencies))]
    public void HarmonicTone_IsWithinThreeCentsWithoutOctaveError(double hz)
    {
        var readings = TunerTestSignals.Run(Tone(hz, 2.0, true));

        AssertSettled(readings, hz, 2000);
    }

    [Theory]
    [InlineData(58.27, 880.0)]
    [InlineData(29.14, 2093.0)]
    [InlineData(110.0, 440.0)]
    public void NoteChange_LowToHighAndBack_ReportsTheNewNote(double lowHz, double highHz)
    {
        const double Part = 1.2;
        foreach (var rich in new[] { false, true })
        {
            var signal = Tone(lowHz, Part, rich)
                .Concat(Tone(highHz, Part, rich))
                .Concat(Tone(lowHz, Part, rich))
                .ToArray();

            var readings = TunerTestSignals.Run(signal);

            AssertPart(readings, lowHz, 0.0, Part);
            AssertPart(readings, highHz, Part, 2 * Part);
            AssertPart(readings, lowHz, 2 * Part, 3 * Part);
        }
    }

    private static void AssertPart(List<(double TimeMs, TunerReading Reading)> readings, double hz, double fromS, double toS)
    {
        var reading = At(readings, toS * 1000 - 50) ?? default;
        Assert.True(reading.HasPitch, $"no pitch at the end of the {hz} Hz part");
        Assert.InRange(TunerTestSignals.CentsBetween(reading.Hz, hz), -3.0, 3.0);
    }

    [Fact]
    public void Silence_ReportsNoPitch()
    {
        var readings = TunerTestSignals.Run(Tone(58.27, 1.0, true).Concat(TunerTestSignals.Silence(1.0)).ToArray());

        Assert.False(readings[^1].Reading.HasPitch);
    }

    [Fact]
    public void FallbackRate44100_ReadsLowAndHigh()
    {
        foreach (var hz in new[] { 41.2, 58.27, 880.0 })
        {
            var data = new float[44100 * 2];
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = (float)(0.3 * Math.Sin(2 * Math.PI * hz * i / 44100));
            }

            var results = new List<TunerReading>();
            var engine = new TunerEngine(44100, results.Add);
            for (var p = 0; p < data.Length; p += 441)
            {
                engine.ProcessBlock(data.AsSpan(p, Math.Min(441, data.Length - p)));
            }

            Assert.NotEmpty(results);
            Assert.True(results[^1].HasPitch, $"{hz} Hz: no pitch");
            Assert.InRange(TunerTestSignals.CentsBetween(results[^1].Hz, hz), -3.0, 3.0);
        }
    }
}
