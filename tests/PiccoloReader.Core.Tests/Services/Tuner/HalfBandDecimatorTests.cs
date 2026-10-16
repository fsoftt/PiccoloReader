using PiccoloReader.Core.Services.Tuner;

namespace PiccoloReader.Core.Tests.Services.Tuner;

public class HalfBandDecimatorTests
{
    private static float[] Decimate(HalfBandDecimator sut, float[] input, int block)
    {
        var result = new List<float>();
        var scratch = new float[HalfBandDecimator.MaxOutputLength(block)];
        for (var i = 0; i < input.Length; i += block)
        {
            var count = sut.Process(input.AsSpan(i, Math.Min(block, input.Length - i)), scratch);
            result.AddRange(scratch.Take(count));
        }

        return result.ToArray();
    }

    [Theory]
    [InlineData(440)]
    [InlineData(1000)]
    [InlineData(2500)]
    [InlineData(4200)]
    public void Passband_PreservesAmplitude(double hz)
    {
        var input = TunerTestSignals.Sine(hz, 0.5, 1.0);

        var output = Decimate(new HalfBandDecimator(), input, 480);

        Assert.Equal(input.Length / 2, output.Length);
        var peak = output.Skip(100).Max(Math.Abs);
        Assert.InRange(peak, 0.97, 1.03);
    }

    [Fact]
    public void StopBand_IsAttenuated()
    {
        var input = TunerTestSignals.Sine(20000, 0.5, 1.0);

        var output = Decimate(new HalfBandDecimator(), input, 480);

        Assert.True(output.Skip(100).Max(Math.Abs) < 0.1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(480)]
    [InlineData(1001)]
    public void Streaming_EqualsOneShot(int block)
    {
        var input = TunerTestSignals.Harmonic(700, 0.3);

        var oneShot = Decimate(new HalfBandDecimator(), input, input.Length);
        var streamed = Decimate(new HalfBandDecimator(), input, block);

        Assert.Equal(oneShot.Length, streamed.Length);
        for (var i = 0; i < oneShot.Length; i++)
        {
            Assert.Equal(oneShot[i], streamed[i], 6);
        }
    }

    [Fact]
    public void DcPasses_WithUnityGain()
    {
        var input = Enumerable.Repeat(0.5f, 200).ToArray();

        var output = Decimate(new HalfBandDecimator(), input, 50);

        Assert.Equal(0.5f, output[^1], 4);
    }
}
