using PiccoloReader.Core.Services.Piano;

namespace PiccoloReader.Core.Tests.Services.Piano;

public class PianoServiceTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(60);

    private readonly FakePianoAudioOutput _output = new();

    private PianoService Create(Func<Task<Stream>>? open = null) => new(_output, open ?? PianoAsset.OpenAsync, Short);

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
