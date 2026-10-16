using PiccoloReader.Core.Services.Tuner;

namespace PiccoloReader.Core.Tests.Services.Tuner;

public class PitchSmootherTests
{
    private const float FrameMs = 512 * 1000f / 24000f;

    private static PitchEstimate Pitch(float hz, float clarity = 0.95f) => new(hz, clarity, 0.2f, true);

    private static readonly PitchEstimate Nothing = PitchEstimate.None(0f);

    [Fact]
    public void NewPitch_NeedsThreeConsecutiveFrames()
    {
        var sut = new PitchSmoother(FrameMs);

        Assert.False(sut.Update(Pitch(880)).HasPitch);
        Assert.False(sut.Update(Pitch(880)).HasPitch);
        var third = sut.Update(Pitch(880));

        Assert.True(third.HasPitch);
        Assert.Equal(880f, third.Hz, 0.5f);
    }

    [Fact]
    public void ScatteredFrames_NeverLock()
    {
        var sut = new PitchSmoother(FrameMs);

        for (var i = 0; i < 20; i++)
        {
            Assert.False(sut.Update(Pitch(i % 2 == 0 ? 1000 : 1500)).HasPitch);
        }
    }

    [Fact]
    public void BrokenRun_RestartsTheCount()
    {
        var sut = new PitchSmoother(FrameMs);

        sut.Update(Pitch(880));
        sut.Update(Pitch(880));
        sut.Update(Nothing);

        Assert.False(sut.Update(Pitch(880)).HasPitch);
    }

    [Fact]
    public void LowClarity_IsRejected()
    {
        var sut = new PitchSmoother(FrameMs);

        for (var i = 0; i < 10; i++)
        {
            Assert.False(sut.Update(Pitch(880, 0.5f)).HasPitch);
        }
    }

    [Fact]
    public void SingleOutlier_IsIgnoredWhileHolding()
    {
        var sut = new PitchSmoother(FrameMs);
        for (var i = 0; i < 6; i++)
        {
            sut.Update(Pitch(880));
        }

        var outlier = sut.Update(Pitch(1000));
        var after = sut.Update(Pitch(880));

        Assert.True(outlier.HasPitch);
        Assert.Equal(880f, outlier.Hz, 1f);
        Assert.Equal(880f, after.Hz, 1f);
    }

    [Fact]
    public void NewStableNote_ReplacesHeldOneAfterFiveFrames()
    {
        var sut = new PitchSmoother(FrameMs);
        for (var i = 0; i < 6; i++)
        {
            sut.Update(Pitch(880));
        }

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(880f, sut.Update(Pitch(1000)).Hz, 1f);
        }

        var reading = sut.Update(Pitch(1000));

        Assert.Equal(1000f, reading.Hz, 1f);
    }

    [Fact]
    public void Silence_IsReportedOnlyAfterHoldTime()
    {
        var sut = new PitchSmoother(FrameMs);
        for (var i = 0; i < 6; i++)
        {
            sut.Update(Pitch(880));
        }

        var framesHeld = 0;
        while (sut.Update(Nothing).HasPitch)
        {
            framesHeld++;
            Assert.True(framesHeld < 20);
        }

        // The hold is ~150 ms: 8 frames at 21.3 ms, the 8th reports silence.
        Assert.InRange((framesHeld + 1) * FrameMs, 140f, 175f);
    }

    [Fact]
    public void Smoothing_ConvergesWithoutJumping()
    {
        var sut = new PitchSmoother(FrameMs);
        for (var i = 0; i < 6; i++)
        {
            sut.Update(Pitch(880));
        }

        var before = sut.Update(Pitch(880)).CentsFromA4;
        var step = sut.Update(Pitch((float)(880 * Math.Pow(2, 20 / 1200.0)))).CentsFromA4;

        // 20 cents away: the median of 3 holds it back, the EMA only moves part of the way.
        Assert.InRange(step - before, 0f, 7f);
    }
}
