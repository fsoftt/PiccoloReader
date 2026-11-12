using Microsoft.JSInterop;
using PiccoloReader.Core.Data;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Web.Services;

// Core's AppDatabase (sqlite-net) runs on a file in the WASM in-memory FS.
// On startup the bytes are restored from OPFS; after writes, ScheduleSave()
// debounces a copy of the file back to OPFS. FlushAsync() writes at once and is
// called when the page is hidden or closed, so the debounce cannot lose a change.
public sealed class PersistentDatabase(IAppStorageProvider storage, BrowserInterop browser)
{
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private CancellationTokenSource? _debounce;

    public AppDatabase Database { get; } = new(storage.DatabasePath);

    public async Task InitializeAsync()
    {
        var existing = await browser.ReadFileAsync(WebStorageProvider.DatabaseFileName);
        if (existing is { Length: > 0 })
        {
            await File.WriteAllBytesAsync(storage.DatabasePath, existing);
        }
        await Database.InitializeAsync();
        ScheduleSave();
    }

    [JSInvokable]
    public async Task FlushAsync()
    {
        _debounce?.Cancel();
        await SaveAsync(CancellationToken.None);
    }

    // Raised for every library change (not for bookkeeping writes), so the linked folder can mirror it.
    public event Action? Changed;

    public void ScheduleSave(bool notify = true)
    {
        if (notify) Changed?.Invoke();
        _debounce?.Cancel();
        var cts = _debounce = new CancellationTokenSource();
        _ = SaveAfterDelayAsync(cts.Token);
    }

    private async Task SaveAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            await SaveAsync(token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SaveAsync(CancellationToken token)
    {
        await _saveLock.WaitAsync(token);
        try
        {
            var bytes = await File.ReadAllBytesAsync(storage.DatabasePath, token);
            await browser.WriteFileAsync(WebStorageProvider.DatabaseFileName, bytes);
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
