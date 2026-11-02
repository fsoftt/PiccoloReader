using PiccoloReader.Core.Services.Piano;

namespace PiccoloReader.Core.Tests.Services.Piano;

public class PianoSynthEngineTests
{
    private const int SampleRate = 48000;
    private const int Block = 256;

    private static PianoSynthEngine Create(string instrumentId = "grand") =>
        new(PianoAsset.SoundFont, SampleRate, PianoInstruments.ById(instrumentId));

    // Renders whole blocks for about the given seconds and returns the peak of the last second
    // (or of everything when shorter), plus whether any sample was NaN or beyond full scale.
    private static (float Peak, bool Invalid) Render(PianoSynthEngine engine, double seconds, double tailSeconds = 1.0)
    {
        var left = new float[Block];
        var right = new float[Block];
        var blocks = (int)(seconds * SampleRate / Block);
        var tailStart = blocks - (int)(tailSeconds * SampleRate / Block);
        var peak = 0f;
        var invalid = false;
        for (var b = 0; b < blocks; b++)
        {
            engine.Render(left, right);
            for (var i = 0; i < Block; i++)
            {
                invalid |= float.IsNaN(left[i]) || float.IsNaN(right[i]) || MathF.Abs(left[i]) > 1f || MathF.Abs(right[i]) > 1f;
                if (b >= tailStart)
                {
                    peak = MathF.Max(peak, MathF.Max(MathF.Abs(left[i]), MathF.Abs(right[i])));
                }
            }
        }

        return (peak, invalid);
    }

    [Fact]
    public void Render_NoNotes_IsSilent()
    {
        var sut = Create();

        var (peak, invalid) = Render(sut, 0.5);

        Assert.Equal(0f, peak);
        Assert.False(invalid);
    }

    [Fact]
    public void NoteOn_ProducesSound()
    {
        var sut = Create();

        sut.NoteOn(69, 100);
        var (peak, invalid) = Render(sut, 0.5, 0.5);

        Assert.True(peak > 0.05f, $"peak {peak}");
        Assert.False(invalid);
    }

    [Fact]
    public void NoteOff_AfterTheReleaseTime_IsSilent()
    {
        var sut = Create("organ");
        sut.NoteOn(60, 100);
        Render(sut, 0.5);

        sut.NoteOff(60);
        var (peak, _) = Render(sut, 4.0);

        Assert.True(peak < 0.001f, $"peak {peak}");
    }

    [Fact]
    public void Sustain_KeepsAReleasedNoteSoundingUntilLifted()
    {
        var sut = Create("organ");
        sut.SetSustain(true);
        sut.NoteOn(60, 100);
        Render(sut, 0.5);
        sut.NoteOff(60);

        var (held, _) = Render(sut, 4.0);
        sut.SetSustain(false);
        var (released, _) = Render(sut, 4.0);

        Assert.True(held > 0.05f, $"held {held}");
        Assert.True(released < 0.001f, $"released {released}");
    }

    [Fact]
    public void SetInstrument_ReleasesTheSoundingNotes()
    {
        var sut = Create("organ");
        sut.NoteOn(60, 100);
        Render(sut, 0.5);

        sut.SetInstrument(PianoInstruments.ById("flute"));
        var (peak, _) = Render(sut, 4.0);

        Assert.True(peak < 0.001f, $"peak {peak}");
    }

    [Fact]
    public void SetInstrument_NewNotesUseTheNewSound()
    {
        var sut = Create("organ");
        sut.SetInstrument(PianoInstruments.ById("flute"));
        sut.NoteOn(72, 100);

        var (peak, invalid) = Render(sut, 0.5, 0.5);

        Assert.True(peak > 0.05f, $"peak {peak}");
        Assert.False(invalid);
    }

    [Fact]
    public void Silence_StopsEverythingImmediatelyAndLiftsThePedal()
    {
        var sut = Create("organ");
        sut.SetSustain(true);
        sut.NoteOn(60, 100);
        Render(sut, 0.3);

        sut.Silence();
        var (peak, _) = Render(sut, 2.5, 0.25);

        Assert.True(peak < 0.001f, $"peak {peak}");
    }

    [Fact]
    public void Chord_TenLoudNotes_NeverExceedsFullScale()
    {
        foreach (var id in new[] { "grand", "guitar", "organ" })
        {
            var sut = Create(id);
            for (var note = 48; note < 58; note++)
            {
                sut.NoteOn(note, 127);
            }

            var (peak, invalid) = Render(sut, 1.0, 1.0);

            Assert.False(invalid, id);
            Assert.True(peak > 0.1f && peak <= 1f, $"{id} peak {peak}");
        }
    }

    [Fact]
    public void Render_ZeroLengthBuffers_DoNotThrow()
    {
        var sut = Create();

        sut.Render(Span<float>.Empty, Span<float>.Empty);
    }
}
