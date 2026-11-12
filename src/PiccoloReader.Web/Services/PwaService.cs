using Microsoft.JSInterop;

namespace PiccoloReader.Web.Services;

public sealed record StorageInfo(double Usage, double Quota, bool Persisted, bool CanPersist);

// wwwroot/js/pwa.js: service worker + update flow, install prompt, storage estimate, incoming
// files (drag-drop, launch queue, share target) and the backup download.
public sealed class PwaService(IJSRuntime js)
{
    private readonly Lazy<Task<IJSObjectReference>> _module = new(() =>
        js.InvokeAsync<IJSObjectReference>("import", "./js/pwa.js").AsTask());

    private IJSInProcessObjectReference? _inProc;

    public bool UpdateReady { get; private set; }

    public bool CanInstall { get; private set; }

    public bool IsStandalone { get; private set; }

    public bool IsIos { get; private set; }

    // iOS Safari has no install prompt: the app is added through Share > Add to Home Screen.
    public bool NeedsIosInstallHelp => IsIos && !IsStandalone;

    public event Action? Changed;

    public event Action? IncomingFiles;

    private async Task<IJSObjectReference> ModuleAsync() => await _module.Value;

    public async Task InitializeAsync()
    {
        var m = await ModuleAsync();
        _inProc = m as IJSInProcessObjectReference;
        await m.InvokeVoidAsync("init", DotNetObjectReference.Create(this));
        IsStandalone = await m.InvokeAsync<bool>("isStandalone");
        IsIos = await m.InvokeAsync<bool>("isIos");
        CanInstall = await m.InvokeAsync<bool>("canInstall");
        await m.InvokeVoidAsync("setDropText", WebText.DropPdfs);
    }

    [JSInvokable]
    public void OnUpdateReady() { UpdateReady = true; Changed?.Invoke(); }

    [JSInvokable]
    public async Task OnInstallStateChanged()
    {
        var m = await ModuleAsync();
        CanInstall = await m.InvokeAsync<bool>("canInstall");
        IsStandalone = await m.InvokeAsync<bool>("isStandalone");
        Changed?.Invoke();
    }

    // The page was hidden or shown again: the tuner stops listening while hidden (like Android's window stop).
    public event Action<bool>? VisibilityChanged;

    [JSInvokable]
    public void OnVisibilityChanged(bool hidden) => VisibilityChanged?.Invoke(hidden);

    [JSInvokable]
    public void OnIncomingFiles() => IncomingFiles?.Invoke();

    public async Task ApplyUpdateAsync() => await (await ModuleAsync()).InvokeVoidAsync("applyUpdate");

    public async Task SetDropTextAsync(string text) => await (await ModuleAsync()).InvokeVoidAsync("setDropText", text);

    public async Task<double[]> CoachMeasureAsync(string selector) => await (await ModuleAsync()).InvokeAsync<double[]>("coachMeasure", selector);

    public async Task<bool> PromptInstallAsync() => await (await ModuleAsync()).InvokeAsync<bool>("promptInstall");

    public async Task<StorageInfo> GetStorageAsync()
    {
        var info = await (await ModuleAsync()).InvokeAsync<StorageJson>("storageInfo");
        return new StorageInfo(info.Usage, info.Quota, info.Persisted, info.CanPersist);
    }

    // Files from the share target cache (the service worker stored them) join the incoming queue.
    public async Task CollectSharedAsync() => await (await ModuleAsync()).InvokeVoidAsync("collectShared");

    public async Task<int> IncomingCountAsync() => await (await ModuleAsync()).InvokeAsync<int>("incomingCount");

    public async Task<string?> IncomingNameAsync() => await (await ModuleAsync()).InvokeAsync<string?>("incomingName");

    public async Task<byte[]?> TakeIncomingAsync() => await (await ModuleAsync()).InvokeAsync<byte[]?>("takeIncoming");

    public async Task<string> PreferencesJsonAsync() => await (await ModuleAsync()).InvokeAsync<string>("localKeys");

    public async Task DownloadBeginAsync() => await (await ModuleAsync()).InvokeVoidAsync("downloadBegin");

    public async Task DownloadEndAsync(string fileName) => await (await ModuleAsync()).InvokeVoidAsync("downloadEnd", fileName);

    // Synchronous chunk append: the zip writer calls Stream.Write, which cannot await.
    // Safe: Blazor WebAssembly runs in-process with the JS runtime.
    public void DownloadChunk(byte[] bytes) => _inProc!.InvokeVoid("downloadChunk", bytes);

    private sealed record StorageJson(double Usage, double Quota, bool Persisted, bool CanPersist);
}
