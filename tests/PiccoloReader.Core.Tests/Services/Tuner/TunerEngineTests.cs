using PiccoloReader.Core.Services.Tuner;
using Xunit.Abstractions;

namespace PiccoloReader.Core.Tests.Services.Tuner;

public class TunerEngineTests(ITestOutputHelper output)
{
    public static TheoryData<double, double> Notes()
    {
        var data = new TheoryData<double, double>();
        // D5, A5, E6, C7 and C8, each in tune and detuned by +-13 cents.
        foreach (var midi in new[] { 74, 81, 88, 96, 108 })
        {
            foreach (var detune in new[] { 0.0, 13.0, -13.0 })
            {
                data.Add(midi, detune);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Notes))]
    public void Sine_IsWithinOneCent(double midi, double detuneCents)
    {
        var hz = TunerTestSignals.HzFor(midi + detuneCents / 100);

        var readings = TunerTestSignals.Run(TunerTestSignals.Sine(hz, 1.0));

        Assert.NotEmpty(readings);
        var last = readings[^1].Reading;
        Assert.True(last.HasPitch);
        Assert.InRange(TunerTestSignals.CentsBetween(last.Hz, hz), -1.0, 1.0);
    }

    [Theory]
    [InlineData(74, 7.0)]
    [InlineData(96, -13.0)]
    [InlineData(108, 13.0)]
    public void Sine_WithA4442_IsWithinOneCent(double midi, double detuneCents)
    {
        var hz = TunerTestSignals.HzFor(midi + detuneCents / 100, 442);

        var readings = TunerTestSignals.Run(TunerTestSignals.Sine(hz, 1.0), new TunerSettings { A4Hz = 442 });

        var last = readings[^1].Reading;
        Assert.True(last.HasPitch);
        Assert.InRange(TunerTestSignals.CentsBetween(last.Hz, hz), -1.0, 1.0);
    }

    [Theory]
    [InlineData(74)]
    [InlineData(81)]
    [InlineData(88)]
    [InlineData(93)]
    public void HarmonicTone_HasNoOctaveError(int midi)
    {
        var hz = TunerTestSignals.HzFor(midi);

        var readings = TunerTestSignals.Run(TunerTestSignals.Harmonic(hz, 1.0));

        Assert.NotEmpty(readings);
        foreach (var (_, reading) in readings.Where(r => r.Reading.HasPitch))
        {
            Assert.InRange(TunerTestSignals.CentsBetween(reading.Hz, hz), -5.0, 5.0);
        }

        Assert.True(readings[^1].Reading.HasPitch);
    }

    [Fact]
    public void Silence_ReportsNoPitch()
    {
        var readings = TunerTestSignals.Run(TunerTestSignals.Silence(2));

        Assert.DoesNotContain(readings, r => r.Reading.HasPitch);
    }

    [Fact]
    public void WhiteNoise_ReportsNoPitch()
    {
        var readings = TunerTestSignals.Run(TunerTestSignals.Noise(3));

        Assert.DoesNotContain(readings, r => r.Reading.HasPitch);
    }

    [Fact]
    public void MetronomeClicksOverSteadyTone_NeverShiftReading()
    {
        var signal = TunerTestSignals.Sine(880, 6, 0.1);
        TunerTestSignals.MixClicks(signal, 500, 0.6);

        var readings = TunerTestSignals.Run(signal);

        // Readings are only emitted on change, so any emission at all must stay on the tone.
        Assert.NotEmpty(readings);
        foreach (var (_, reading) in readings)
        {
            Assert.True(reading.HasPitch);
            Assert.InRange(TunerTestSignals.CentsBetween(reading.Hz, 880), -3.0, 3.0);
        }
    }

    [Fact]
    public void ClickTrainAlone_NeverReportsPitch()
    {
        var signal = TunerTestSignals.Silence(6);
        TunerTestSignals.MixClicks(signal, 500, 0.6);

        var readings = TunerTestSignals.Run(signal);

        Assert.DoesNotContain(readings, r => r.Reading.HasPitch);
    }

    [Fact]
    public void FastClickTrainAlone_NeverReportsPitch()
    {
        var signal = TunerTestSignals.Silence(6);
        TunerTestSignals.MixClicks(signal, 200, 0.8, 30);

        var readings = TunerTestSignals.Run(signal);

        Assert.DoesNotContain(readings, r => r.Reading.HasPitch);
    }

    [Fact]
    public void Readings_AreAtLeast50MsApart()
    {
        var signal = TunerTestSignals.Sine(1000, 3);
        // A slow glide keeps cents changing so every allowed slot is a candidate.
        for (var i = 0; i < signal.Length; i++)
        {
            var hz = 1000 * Math.Pow(2, i / (double)signal.Length * 0.03);
            signal[i] = (float)(0.3 * Math.Sin(2 * Math.PI * hz * i / TunerTestSignals.SampleRate));
        }

        var readings = TunerTestSignals.Run(signal);

        Assert.True(readings.Count > 3);
        for (var i = 1; i < readings.Count; i++)
        {
            Assert.True(readings[i].TimeMs - readings[i - 1].TimeMs >= 50, $"{readings[i].TimeMs - readings[i - 1].TimeMs} ms");
        }
    }

    [Fact]
    public void SteadyTone_DoesNotKeepEmitting()
    {
        var readings = TunerTestSignals.Run(TunerTestSignals.Sine(1000, 4));

        // Lock once, maybe a few small refinements; never one per slot (4 s / 50 ms = 80).
        Assert.InRange(readings.Count, 1, 6);
    }

    [Fact]
    public void ToneThenSilence_EmitsPitchThenNoPitch()
    {
        var tone = TunerTestSignals.Sine(1000, 1);
        var signal = tone.Concat(TunerTestSignals.Silence(1)).ToArray();

        var readings = TunerTestSignals.Run(signal);

        Assert.True(readings[0].Reading.HasPitch);
        Assert.False(readings[^1].Reading.HasPitch);
    }

    [Fact]
    public void Pcm16Overload_MatchesFloat()
    {
        var floats = TunerTestSignals.Sine(1318.5, 1);
        var pcm = floats.Select(f => (short)Math.Round(f * 32767)).ToArray();
        TunerReading? last = null;
        var engine = new TunerEngine(TunerTestSignals.SampleRate, null, r => last = r);

        for (var i = 0; i < pcm.Length; i += 480)
        {
            engine.ProcessBlock(pcm.AsSpan(i, Math.Min(480, pcm.Length - i)));
        }

        Assert.NotNull(last);
        Assert.InRange(TunerTestSignals.CentsBetween(last.Value.Hz, 1318.5), -1.0, 1.0);
    }

    [Fact]
    public void Works_At44100()
    {
        var data = new float[44100];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (float)(0.3 * Math.Sin(2 * Math.PI * 1760 * i / 44100.0));
        }

        TunerReading last = default;
        var engine = new TunerEngine(44100, null, r => last = r);
        for (var i = 0; i < data.Length; i += 441)
        {
            engine.ProcessBlock(data.AsSpan(i, Math.Min(441, data.Length - i)));
        }

        Assert.True(last.HasPitch);
        Assert.InRange(TunerTestSignals.CentsBetween(last.Hz, 1760), -1.0, 1.0);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var count = 0;
        TunerReading last = default;
        var engine = new TunerEngine(TunerTestSignals.SampleRate, null, r => { count++; last = r; });
        var tone = TunerTestSignals.Sine(1000, 1);
        engine.ProcessBlock(tone);
        Assert.True(last.HasPitch);

        engine.Reset();
        engine.ProcessBlock(TunerTestSignals.Silence(1));

        Assert.True(last.HasPitch);
        Assert.Equal(1, count);
    }

    [Fact]
    public void UpdateSettings_IsVisibleToEngine()
    {
        var engine = new TunerEngine(TunerTestSignals.SampleRate);

        engine.UpdateSettings(new TunerSettings { A4Hz = 442 });

        Assert.Equal(442, engine.Settings.A4Hz);
    }

    [Fact]
    public void ProcessBlock_AllocatesNothingAfterWarmUp()
    {
        var signal = TunerTestSignals.Sine(880, 5);
        TunerTestSignals.MixClicks(signal, 500, 0.5);
        var pcm = signal.Select(f => (short)Math.Round(f * 32767)).ToArray();
        var emitted = 0;
        var engine = new TunerEngine(TunerTestSignals.SampleRate, null, _ => emitted++);
        Feed(engine, signal, pcm);

        var before = GC.GetAllocatedBytesForCurrentThread();
        Feed(engine, signal, pcm);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(emitted > 0);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ProcessBlock_CostIsFarBelowRealTime()
    {
        // Worst cases for YIN: noise never hits the threshold so every lag is computed.
        var noise = TunerTestSignals.Noise(10);
        var tone = TunerTestSignals.Sine(587.33, 10);
        var engine = new TunerEngine(TunerTestSignals.SampleRate);
        Feed(engine, tone, null);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        Feed(engine, noise, null);
        Feed(engine, tone, null);
        watch.Stop();

        output.WriteLine($"Processed 10 s noise + 10 s low tone (48 kHz) in {watch.ElapsedMilliseconds} ms");
        Assert.True(watch.ElapsedMilliseconds < 1000, $"took {watch.ElapsedMilliseconds} ms");
    }

    private static void Feed(TunerEngine engine, float[] floats, short[]? pcm)
    {
        for (var i = 0; i < floats.Length; i += TunerTestSignals.BlockSize)
        {
            var count = Math.Min(TunerTestSignals.BlockSize, floats.Length - i);
            engine.ProcessBlock(floats.AsSpan(i, count));
            if (pcm is not null)
            {
                engine.ProcessBlock(pcm.AsSpan(i, count));
            }
        }
    }
}
