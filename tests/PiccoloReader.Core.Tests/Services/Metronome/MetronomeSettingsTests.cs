using PiccoloReader.Core.Services.Metronome;

namespace PiccoloReader.Core.Tests.Services.Metronome;

public class MetronomeSettingsTests
{
    [Fact]
    public void Defaults_AreHundredBpmFourBeatsAccentNoSubdivision()
    {
        var sut = new MetronomeSettings();

        Assert.Equal(100, sut.Bpm);
        Assert.Equal(4, sut.BeatsPerBar);
        Assert.True(sut.AccentFirstBeat);
        Assert.Equal(1, sut.Subdivision);
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(29, 30)]
    [InlineData(30, 30)]
    [InlineData(250, 250)]
    [InlineData(251, 250)]
    [InlineData(9999, 250)]
    public void Bpm_IsClamped(int value, int expected) =>
        Assert.Equal(expected, new MetronomeSettings { Bpm = value }.Bpm);

    [Theory]
    [InlineData(-3, 1)]
    [InlineData(0, 1)]
    [InlineData(12, 12)]
    [InlineData(13, 12)]
    public void BeatsPerBar_IsClamped(int value, int expected) =>
        Assert.Equal(expected, new MetronomeSettings { BeatsPerBar = value }.BeatsPerBar);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(4, 4)]
    [InlineData(5, 4)]
    public void Subdivision_IsClamped(int value, int expected) =>
        Assert.Equal(expected, new MetronomeSettings { Subdivision = value }.Subdivision);

    [Fact]
    public void With_StillClamps() =>
        Assert.Equal(250, (new MetronomeSettings() with { Bpm = 1000 }).Bpm);
}
