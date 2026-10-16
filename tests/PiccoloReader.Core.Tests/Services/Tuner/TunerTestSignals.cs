using PiccoloReader.Core.Services.Metronome;
using PiccoloReader.Core.Services.Tuner;

namespace PiccoloReader.Core.Tests.Services.Tuner;

internal static class TunerTestSignals
{
    public const int SampleRate = 48000;
    public const int BlockSize = 480;

    public static double HzFor(double midi, double a4 = 440) => a4 * Math.Pow(2, (midi - 69) / 12);

    public static double CentsBetween(double hz, double reference) => 1200 * Math.Log2(hz / reference);

    public static float[] Sine(double hz, double seconds, double amplitude = 0.3)
    {
        var data = new float[(int)(SampleRate * seconds)];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / SampleRate));
        }

        return data;
    }

    /// <summary>Fundamental plus harmonics 2..5 with decreasing amplitude.</summary>
    public static float[] Harmonic(double hz, double seconds, double amplitude = 0.25)
    {
        var data = new float[(int)(SampleRate * seconds)];
        for (var i = 0; i < data.Length; i++)
        {
            double v = 0;
            for (var h = 1; h <= 5; h++)
            {
                if (hz * h < SampleRate / 2.5)
                {
                    v += Math.Sin(2 * Math.PI * hz * h * i / SampleRate) / h;
                }
            }

            data[i] = (float)(amplitude * v);
        }

        return data;
    }

    public static float[] Noise(double seconds, double amplitude = 0.3, int seed = 1234)
    {
        var random = new Random(seed);
        var data = new float[(int)(SampleRate * seconds)];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (float)(amplitude * (random.NextDouble() * 2 - 1));
        }

        return data;
    }

    /// <summary>Adds metronome-like click bursts (ClickSynth) at the given period, first one after one period.</summary>
    public static void MixClicks(float[] signal, double periodMs, double amplitude, double durationMs = 25)
    {
        var periodSamples = (int)(SampleRate * periodMs / 1000);
        var toggle = false;
        for (var start = periodSamples / 2; start < signal.Length; start += periodSamples)
        {
            var click = ClickSynth.Create(SampleRate, toggle ? 1500 : 1000, amplitude, durationMs);
            toggle = !toggle;
            for (var i = 0; i < click.Length && start + i < signal.Length; i++)
            {
                signal[start + i] += click[i] / 32768f;
            }
        }
    }

    public static float[] Silence(double seconds) => new float[(int)(SampleRate * seconds)];

    public static List<(double TimeMs, TunerReading Reading)> Run(float[] signal)
    {
        var results = new List<(double, TunerReading)>();
        var position = 0;
        var engine = new TunerEngine(SampleRate, r => results.Add((position * 1000.0 / SampleRate, r)));
        for (position = 0; position < signal.Length; position += BlockSize)
        {
            var count = Math.Min(BlockSize, signal.Length - position);
            engine.ProcessBlock(signal.AsSpan(position, count));
        }

        return results;
    }
}
