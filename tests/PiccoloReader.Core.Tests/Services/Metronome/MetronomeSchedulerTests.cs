using PiccoloReader.Core.Services.Metronome;

namespace PiccoloReader.Core.Tests.Services.Metronome;

public class MetronomeSchedulerTests
{
    private const short NormalValue = 100;
    private const short AccentValue = 200;
    private const short SubValue = 50;

    private static ClickSet ImpulseClicks() => new([NormalValue], [AccentValue], [SubValue]);

    private static List<(long Frame, short Value)> Render(
        MetronomeScheduler scheduler,
        long totalFrames,
        int chunkFrames,
        Func<long, MetronomeSettings> settingsAt)
    {
        var clicks = new List<(long, short)>();
        var buffer = new short[chunkFrames];
        while (scheduler.Position < totalFrames)
        {
            var start = scheduler.Position;
            scheduler.Fill(buffer, chunkFrames, settingsAt(start));
            for (var i = 0; i < chunkFrames; i++)
            {
                if (buffer[i] != 0)
                {
                    clicks.Add((start + i, buffer[i]));
                }
            }
        }

        return clicks;
    }

    [Fact]
    public void Fill_120Bpm48kOverTenMinutes_BeatsLandExactlyWithoutDrift()
    {
        var scheduler = new MetronomeScheduler(48000, ImpulseClicks());
        var settings = new MetronomeSettings { Bpm = 120 };
        const long tenMinutes = 48000L * 60 * 10;

        var clicks = Render(scheduler, tenMinutes, 960, _ => settings);

        Assert.Equal(1200, clicks.Count);
        for (var i = 0; i < clicks.Count; i++)
        {
            Assert.Equal(24000L * i, clicks[i].Frame);
        }
    }

    [Fact]
    public void Fill_NonDivisibleTempo_PlacesBeatsAtExactRationalPositions()
    {
        const int sampleRate = 44100;
        const int bpm = 130;
        var scheduler = new MetronomeScheduler(sampleRate, ImpulseClicks());
        var settings = new MetronomeSettings { Bpm = bpm };

        var clicks = Render(scheduler, sampleRate * 60L * 10, 441, _ => settings);

        for (var i = 0; i < clicks.Count; i++)
        {
            Assert.Equal(i * sampleRate * 60L / bpm, clicks[i].Frame);
        }
    }

    [Fact]
    public void Fill_ChunkSizeDoesNotChangePlacement()
    {
        var settings = new MetronomeSettings { Bpm = 97, Subdivision = 3 };
        var small = Render(new MetronomeScheduler(48000, ImpulseClicks()), 48000 * 60, 137, _ => settings);
        var large = Render(new MetronomeScheduler(48000, ImpulseClicks()), 48000 * 60, 2048, _ => settings);

        var common = Math.Min(small.Count, large.Count) - 3;
        Assert.True(common > 100);
        Assert.Equal(small.Take(common), large.Take(common));
    }

    [Fact]
    public void Fill_ClickSplitAcrossChunks_IsContinuous()
    {
        // A 6-frame click that starts 2 frames before the chunk boundary.
        var click = new short[] { 1, 2, 3, 4, 5, 6 };
        var scheduler = new MetronomeScheduler(48000, new ClickSet(click, click, click));
        var settings = new MetronomeSettings { Bpm = 120, AccentFirstBeat = false };
        var first = new short[24000 + 2];
        var second = new short[1000];

        scheduler.Fill(first, first.Length, settings);
        scheduler.Fill(second, second.Length, settings);

        Assert.Equal(new short[] { 1, 2 }, first[24000..]);
        Assert.Equal(new short[] { 3, 4, 5, 6, 0 }, second[..5]);
    }

    [Fact]
    public void Fill_BpmChangeMidBeat_TakesEffectFromNextBeat()
    {
        var scheduler = new MetronomeScheduler(48000, ImpulseClicks());
        var slow = new MetronomeSettings { Bpm = 60 };
        var fast = new MetronomeSettings { Bpm = 120 };

        // The change arrives at frame 72000, halfway through the beat that
        // started at 48000: that beat keeps its length, the next one is fast.
        var clicks = Render(scheduler, 48000 * 4, 480, start => start < 72000 ? slow : fast);

        Assert.Equal(new long[] { 0, 48000, 96000, 120000, 144000 }, clicks.Take(5).Select(c => c.Frame));
    }

    [Fact]
    public void Fill_BpmChangeBeforeBeatStarts_ShortensThatBeat()
    {
        var scheduler = new MetronomeScheduler(48000, ImpulseClicks());
        var slow = new MetronomeSettings { Bpm = 60 };
        var fast = new MetronomeSettings { Bpm = 120 };

        // Fast from frame 20000: the beat starting at 48000 already reads 120 BPM.
        var clicks = Render(scheduler, 48000 * 3, 480, start => start < 20000 ? slow : fast);

        Assert.Equal(new long[] { 0, 48000, 72000, 96000 }, clicks.Take(4).Select(c => c.Frame));
    }

    [Fact]
    public void Fill_AccentOnFirstBeat_OnlyBarStartIsAccented()
    {
        var scheduler = new MetronomeScheduler(48000, ImpulseClicks());
        var settings = new MetronomeSettings { Bpm = 120, BeatsPerBar = 3 };

        var clicks = Render(scheduler, 24000L * 7, 960, _ => settings);

        Assert.Equal(
            new[] { AccentValue, NormalValue, NormalValue, AccentValue, NormalValue, NormalValue, AccentValue },
            clicks.Select(c => c.Value));
    }

    [Fact]
    public void Fill_AccentDisabled_AllBeatsAreNormal()
    {
        var scheduler = new MetronomeScheduler(48000, ImpulseClicks());
        var settings = new MetronomeSettings { Bpm = 120, BeatsPerBar = 2, AccentFirstBeat = false };

        var clicks = Render(scheduler, 24000L * 4, 960, _ => settings);

        Assert.All(clicks, c => Assert.Equal(NormalValue, c.Value));
    }

    [Fact]
    public void Fill_Subdivision_InsertsQuieterClicksEvenlyBetweenBeats()
    {
        var scheduler = new MetronomeScheduler(48000, ImpulseClicks());
        var settings = new MetronomeSettings { Bpm = 120, Subdivision = 3, BeatsPerBar = 4 };

        var clicks = Render(scheduler, 24000 * 2, 960, _ => settings);

        Assert.Equal(
            new[] { (0L, AccentValue), (8000L, SubValue), (16000L, SubValue), (24000L, NormalValue), (32000L, SubValue), (40000L, SubValue) },
            clicks.Select(c => (c.Frame, c.Value)));
    }

    [Fact]
    public void Fill_SubdivisionChange_TakesEffectFromNextBeat()
    {
        var scheduler = new MetronomeScheduler(48000, ImpulseClicks());
        var plain = new MetronomeSettings { Bpm = 120 };
        var doubled = new MetronomeSettings { Bpm = 120, Subdivision = 2 };

        // Beat 0 is already scheduled when the change arrives at frame 1000.
        var clicks = Render(scheduler, 24000 * 2, 960, start => start < 1000 ? plain : doubled);

        Assert.Equal(new long[] { 0, 24000, 36000 }, clicks.Select(c => c.Frame));
    }

    [Fact]
    public void Fill_BeatsPerBarShrinks_WrapsBarIndex()
    {
        var scheduler = new MetronomeScheduler(48000, ImpulseClicks());
        var four = new MetronomeSettings { Bpm = 120, BeatsPerBar = 4 };
        var one = new MetronomeSettings { Bpm = 120, BeatsPerBar = 1 };

        var clicks = Render(scheduler, 24000L * 5, 960, start => start < 24000 * 2 ? four : one);

        // Bar index 0, 1, then every beat is the first beat of a one-beat bar.
        Assert.Equal(
            new[] { AccentValue, NormalValue, AccentValue, AccentValue, AccentValue },
            clicks.Select(c => c.Value));
    }

    [Fact]
    public void BeatEvents_ReportMainBeatsOnly_WithFrameAndBarIndex()
    {
        var scheduler = new MetronomeScheduler(48000, ImpulseClicks());
        var settings = new MetronomeSettings { Bpm = 120, Subdivision = 2, BeatsPerBar = 2 };
        var buffer = new short[30000];

        scheduler.Fill(buffer, buffer.Length, settings);

        Assert.Equal(2, scheduler.BeatEventCount);
        Assert.Equal(new BeatEvent(0, 0, true), scheduler.BeatEvents[0]);
        Assert.Equal(new BeatEvent(24000, 1, false), scheduler.BeatEvents[1]);
    }

    [Fact]
    public void Fill_AdvancesPosition()
    {
        var scheduler = new MetronomeScheduler(48000, ImpulseClicks());
        var buffer = new short[480];

        scheduler.Fill(buffer, 480, new MetronomeSettings());
        scheduler.Fill(buffer, 100, new MetronomeSettings());

        Assert.Equal(580, scheduler.Position);
    }
}
