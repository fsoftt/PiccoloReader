using PiccoloReader.Core.Services.Piano;

namespace PiccoloReader.Core.Tests.Services.Piano;

public class PianoServiceTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(60);

    private readonly FakePianoAudioOutput _output = new();

    private PianoService Create(Func<Task<Stream>>? open = null, TimeSpan? unload = null) =>
        new(_output, open ?? PianoAsset.OpenAsync, Short, unload ?? TimeSpan.FromMinutes(10));

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the condition.");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task BeforeAcquire_NothingIsLoadedOrStarted()
    {
        var opened = 0;
        var sut = Create(() =>
        {
            opened++;
            return PianoAsset.OpenAsync();
        });

        sut.NoteOn(60, 100);
        sut.SetInstrument(PianoInstruments.ById("flute"));
        await Task.Delay(100);

        Assert.Equal(PianoLoadState.NotLoaded, sut.State);
        Assert.Equal(0, opened);
        Assert.Equal(0, _output.StartCount);
    }

    [Fact]
    public async Task Acquire_LoadsOnceAndStartsTheStream()
    {
        var opened = 0;
        var sut = Create(() =>
        {
            opened++;
            return PianoAsset.OpenAsync();
        });

        sut.Acquire();
        await WaitUntil(() => _output.IsRunning);
        sut.Acquire();
        sut.Release();
        sut.Release();
        sut.Acquire();
        await Task.Delay(200);

        Assert.Equal(PianoLoadState.Ready, sut.State);
        Assert.Equal(1, opened);
        Assert.True(_output.IsRunning);
    }

    [Fact]
    public async Task Release_LastLease_StopsTheStreamAfterTheIdleDelay()
    {
        var sut = Create();
        sut.Acquire();
        await WaitUntil(() => _output.IsRunning);

        sut.Release();

        await WaitUntil(() => !_output.IsRunning);
        Assert.Equal(1, _output.StopCount);
    }

    [Fact]
    public async Task Reacquire_WithinTheIdleDelay_KeepsTheSameStream()
    {
        var sut = Create();
        sut.Acquire();
        await WaitUntil(() => _output.IsRunning);

        sut.Release();
        sut.Acquire();
        await Task.Delay(250);

        Assert.True(_output.IsRunning);
        Assert.Equal(1, _output.StartCount);
        Assert.Equal(0, _output.StopCount);
    }

    [Fact]
    public async Task Suspend_StopsTheStreamAndResumeRestartsItWhileLeased()
    {
        var sut = Create();
        sut.Acquire();
        await WaitUntil(() => _output.IsRunning);

        sut.Suspend();
        await WaitUntil(() => !_output.IsRunning);
        sut.Resume();
        await WaitUntil(() => _output.IsRunning);

        Assert.Equal(2, _output.StartCount);
    }

    [Fact]
    public async Task Resume_WithoutLeases_DoesNotStartTheStream()
    {
        var sut = Create();
        sut.Acquire();
        await WaitUntil(() => _output.IsRunning);
        sut.Release();
        await WaitUntil(() => !_output.IsRunning);

        sut.Suspend();
        sut.Resume();
        await Task.Delay(100);

        Assert.False(_output.IsRunning);
    }

    [Fact]
    public async Task LoadFailure_ReportsFailedAndRetriesOnTheNextAcquire()
    {
        var attempts = 0;
        var sut = Create(() => ++attempts == 1 ? throw new IOException("boom") : PianoAsset.OpenAsync());

        sut.Acquire();
        await WaitUntil(() => sut.State == PianoLoadState.Failed);
        sut.Release();
        sut.Acquire();
        await WaitUntil(() => sut.State == PianoLoadState.Ready);

        Assert.Equal(2, attempts);
        await WaitUntil(() => _output.IsRunning);
    }

    [Fact]
    public async Task Load_FromAForwardOnlyStream_Works()
    {
        var sut = Create(async () => new ForwardOnlyStream(await PianoAsset.OpenAsync()));

        sut.Acquire();

        await WaitUntil(() => sut.State == PianoLoadState.Ready);
    }

    [Fact]
    public async Task Died_RestartsTheStreamWhileLeased()
    {
        var sut = Create();
        sut.Acquire();
        await WaitUntil(() => _output.IsRunning);

        _output.RaiseDied();

        await WaitUntil(() => _output.StartCount == 2 && _output.IsRunning);
    }

    [Fact]
    public async Task Died_RestartsAreThrottled()
    {
        var sut = Create();
        sut.Acquire();
        await WaitUntil(() => _output.IsRunning);

        for (var i = 0; i < 5; i++)
        {
            _output.RaiseDied();
        }

        await Task.Delay(300);
        Assert.True(_output.StartCount <= 3, $"starts {_output.StartCount}");
    }

    [Fact]
    public async Task NoteOn_WhileTheStreamIsDown_RestartsItAndDropsTheNote()
    {
        var sut = Create();
        sut.Acquire();
        await WaitUntil(() => _output.IsRunning);
        _output.RaiseDied();
        await WaitUntil(() => _output.StartCount == 2);
        _output.RaiseDied();

        sut.NoteOn(60, 100);

        await WaitUntil(() => _output.StartCount == 3 && _output.IsRunning);
    }

    [Fact]
    public async Task StartFailure_ReportsFailedAndRetriesOnTheNextAcquire()
    {
        _output.StartResult = false;
        var sut = Create();
        sut.Acquire();
        await WaitUntil(() => sut.State == PianoLoadState.Failed);
        sut.Release();

        _output.StartResult = true;
        sut.Acquire();

        await WaitUntil(() => sut.State == PianoLoadState.Ready && _output.IsRunning);
    }

    [Fact]
    public async Task Idle_LongEnough_UnloadsTheSounds()
    {
        var sut = Create(unload: TimeSpan.FromMilliseconds(150));
        sut.Acquire();
        await WaitUntil(() => _output.IsRunning);

        sut.Release();

        await WaitUntil(() => sut.State == PianoLoadState.NotLoaded);
        Assert.False(_output.IsRunning);
    }

    [Fact]
    public async Task Acquire_BeforeTheUnloadDelay_KeepsTheSounds()
    {
        var sut = Create(unload: TimeSpan.FromMilliseconds(400));
        sut.Acquire();
        await WaitUntil(() => _output.IsRunning);
        sut.Release();
        await WaitUntil(() => !_output.IsRunning);

        sut.Acquire();
        await Task.Delay(700);

        Assert.Equal(PianoLoadState.Ready, sut.State);
        Assert.True(_output.IsRunning);
    }

    [Fact]
    public async Task Acquire_AfterUnload_ReloadsTheSounds()
    {
        var opened = 0;
        var sut = Create(
            () =>
            {
                opened++;
                return PianoAsset.OpenAsync();
            },
            TimeSpan.FromMilliseconds(150));
        sut.SetInstrument(PianoInstruments.ById("flute"));
        sut.Acquire();
        await WaitUntil(() => _output.IsRunning);
        sut.Release();
        await WaitUntil(() => sut.State == PianoLoadState.NotLoaded);

        sut.Acquire();

        await WaitUntil(() => sut.State == PianoLoadState.Ready && _output.IsRunning);
        Assert.Equal(2, opened);
    }

    private sealed class ForwardOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task StateChanged_IsRaisedWhenLoadingStartsAndFinishes()
    {
        var sut = Create();
        var states = new List<PianoLoadState>();
        sut.StateChanged += (_, _) =>
        {
            lock (states)
            {
                states.Add(sut.State);
            }
        };

        sut.Acquire();
        await WaitUntil(() => sut.State == PianoLoadState.Ready);

        lock (states)
        {
            Assert.Equal(PianoLoadState.Loading, states[0]);
        }
    }
}
