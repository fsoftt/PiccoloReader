using PiccoloReader.Core.Services.Tuner;

namespace PiccoloReader.Core.Tests.Services.Tuner;

public class BandSelectorTests
{
    private static PitchEstimate Pitch(float hz, float clarity) => new(hz, clarity, 0.1f, true);

    private static PitchEstimate None => PitchEstimate.None(0.1f);

    [Fact]
    public void OnlyOneBand_IsTaken()
    {
        var sut = new BandSelector();

        Assert.Equal(TunerBand.Low, sut.Select(Pitch(60, 0.95f), None));
        Assert.Equal(TunerBand.High, sut.Select(None, Pitch(880, 0.95f)));
        Assert.Equal(TunerBand.None, sut.Select(None, None));
    }

    [Fact]
    public void BelowOverlap_LowWins_AboveOverlap_HighWins()
    {
        var sut = new BandSelector();

        Assert.Equal(TunerBand.Low, sut.Select(Pitch(300, 0.9f), Pitch(400, 0.99f)));
        Assert.Equal(TunerBand.High, sut.Select(Pitch(480, 0.99f), Pitch(520, 0.9f)));
    }

    [Fact]
    public void Overlap_KeepsThePreviousBandUnlessTheOtherIsClearer()
    {
        var sut = new BandSelector();
        sut.Select(Pitch(300, 0.95f), None);

        Assert.Equal(TunerBand.Low, sut.Select(Pitch(440, 0.90f), Pitch(440, 0.93f)));
        Assert.Equal(TunerBand.High, sut.Select(Pitch(440, 0.90f), Pitch(440, 0.96f)));
        Assert.Equal(TunerBand.High, sut.Select(Pitch(440, 0.95f), Pitch(440, 0.97f)));
        Assert.Equal(TunerBand.Low, sut.Select(Pitch(440, 1.0f), Pitch(440, 0.94f)));
    }

    [Fact]
    public void LowLockedOnSeveralPeriodsOfAHighNote_LosesToTheHighBand()
    {
        var sut = new BandSelector();
        sut.Select(None, Pitch(440, 0.99f));

        Assert.Equal(TunerBand.High, sut.Select(Pitch(31.4f, 0.90f), Pitch(440, 0.99f)));
        Assert.Equal(TunerBand.High, sut.Select(Pitch(220, 0.95f), Pitch(440, 0.97f)));
    }

    [Fact]
    public void ClearLowFundamental_BeatsAHarmonicSeenByTheHighBand()
    {
        var sut = new BandSelector();

        Assert.Equal(TunerBand.Low, sut.Select(Pitch(100, 0.98f), Pitch(500, 0.90f)));
    }
}
