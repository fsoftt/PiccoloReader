using PiccoloReader.Core.Services.Metronome;

namespace PiccoloReader.Core.Tests.Services.Metronome;

public class TapTempoTests
{
    [Fact]
    public void FirstTap_ReturnsNull() =>
        Assert.Null(new TapTempo().Tap(1000));

    [Fact]
    public void TwoTapsHalfSecondApart_Is120Bpm()
    {
        var sut = new TapTempo();
        sut.Tap(1000);

        Assert.Equal(120, sut.Tap(1500));
    }

    [Fact]
    public void AveragesOnlyLastFourIntervals()
    {
        var sut = new TapTempo();
        // One slow interval (1000 ms), then four 500 ms intervals.
        long t = 0;
        sut.Tap(t);
        t += 1000;
        sut.Tap(t);
        int? bpm = null;
        for (var i = 0; i < 4; i++)
        {
            t += 500;
            bpm = sut.Tap(t);
        }

        Assert.Equal(120, bpm);
    }

    [Fact]
    public void GapOverTwoSeconds_ResetsSeries()
    {
        var sut = new TapTempo();
        sut.Tap(0);
        sut.Tap(500);

        Assert.Null(sut.Tap(500 + 2001));
        Assert.Equal(60, sut.Tap(500 + 2001 + 1000));
    }

    [Fact]
    public void GapOfExactlyTwoSeconds_DoesNotReset()
    {
        var sut = new TapTempo();
        sut.Tap(0);

        Assert.Equal(30, sut.Tap(2000));
    }

    [Fact]
    public void ResultIsClampedToRange()
    {
        var sut = new TapTempo();
        sut.Tap(0);
        Assert.Equal(250, sut.Tap(50));

        sut.Reset();
        sut.Tap(0);
        Assert.Equal(30, sut.Tap(2000));
    }

    [Fact]
    public void SameTimestampTwice_ReturnsNull()
    {
        var sut = new TapTempo();
        sut.Tap(100);

        Assert.Null(sut.Tap(100));
    }
}
