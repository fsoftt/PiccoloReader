using PiccoloReader.Core.Services.Metronome;

namespace PiccoloReader.Core.Tests.Services.Metronome;

public class ClickSynthTests
{
    private static double Rms(short[] samples, int from, int to)
    {
        double sum = 0;
        for (var i = from; i < to; i++)
        {
            sum += (double)samples[i] * samples[i];
        }

        return Math.Sqrt(sum / (to - from));
    }

    [Theory]
    [InlineData(48000, 1200)]
    [InlineData(44100, 1102)]
    public void Create_LengthMatchesDuration(int sampleRate, int expected) =>
        Assert.Equal(expected, ClickSynth.Create(sampleRate, 1000, 0.8).Length);

    [Fact]
    public void Create_PeakIsCloseToAmplitude()
    {
        var click = ClickSynth.Create(48000, 1000, 0.8);
        var peak = click.Max(s => Math.Abs((int)s));

        Assert.InRange(peak, 0.7 * short.MaxValue, 0.8 * short.MaxValue + 1);
    }

    [Fact]
    public void Create_DecaysToNearSilence()
    {
        var click = ClickSynth.Create(48000, 1000, 0.8);
        var quarter = click.Length / 4;

        Assert.True(Rms(click, click.Length - quarter, click.Length) < Rms(click, 0, quarter) / 20);
        Assert.True(Math.Abs((int)click[^1]) < 0.01 * short.MaxValue);
    }

    [Fact]
    public void Create_HigherFrequencyHasMoreZeroCrossings()
    {
        static int Crossings(short[] s) => Enumerable.Range(1, s.Length - 1).Count(i => (s[i - 1] < 0) != (s[i] < 0));

        Assert.True(Crossings(ClickSynth.Create(48000, 1500, 0.8)) > Crossings(ClickSynth.Create(48000, 1000, 0.8)));
    }

    [Fact]
    public void ClickSet_SubdivisionIsAboutHalfTheNormalLevel()
    {
        var set = new ClickSet(48000);
        var ratio = (double)set.Subdivision.Max(s => Math.Abs((int)s)) / set.Normal.Max(s => Math.Abs((int)s));

        Assert.InRange(ratio, 0.45, 0.55);
    }
}
