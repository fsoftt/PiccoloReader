using Microsoft.JSInterop;

namespace PiccoloReader.Web.Services;

// Thin wrapper over wwwroot/js/storage.js (OPFS, localStorage, persist()).
public sealed class BrowserInterop(IJSRuntime js)
{
    private readonly Lazy<Task<IJSObjectReference>> _module = new(() =>
        js.InvokeAsync<IJSObjectReference>("import", "./js/storage.js").AsTask());

    private async Task<IJSObjectReference> ModuleAsync() => await _module.Value;

    public async Task<bool> RequestPersistentStorageAsync()
        => await (await ModuleAsync()).InvokeAsync<bool>("requestPersist");

    public async Task WriteFileAsync(string name, byte[] data)
        => await (await ModuleAsync()).InvokeVoidAsync("writeFile", name, data);

    public async Task<byte[]?> ReadFileAsync(string name)
        => await (await ModuleAsync()).InvokeAsync<byte[]?>("readFile", name);

    public async Task StageBeginAsync(string name) => await (await ModuleAsync()).InvokeVoidAsync("stageBegin", name);

    public async Task StageAppendAsync(byte[] data) => await (await ModuleAsync()).InvokeVoidAsync("stageAppend", data);

    public async Task StageEndAsync() => await (await ModuleAsync()).InvokeVoidAsync("stageEnd");

    public async Task<long> FileSizeAsync(string name) => (long)await (await ModuleAsync()).InvokeAsync<double>("fileSize", name);

    public async Task<byte[]> ReadRangeAsync(string name, long offset, long length)
        => await (await ModuleAsync()).InvokeAsync<byte[]>("readRange", name, (double)offset, (double)length);

    public async Task DeleteFileAsync(string name) => await (await ModuleAsync()).InvokeVoidAsync("deleteFile", name);

    public async Task<string> GetLanguageAsync()
        => await (await ModuleAsync()).InvokeAsync<string>("getLanguage");

    public async Task RegisterFlushAsync<T>(DotNetObjectReference<T> target) where T : class
        => await (await ModuleAsync()).InvokeVoidAsync("registerFlush", target);

    // Synchronous localStorage access for the sync-shaped Core preference interfaces.
    // Safe here: Blazor WebAssembly runs in-process with the JS runtime.
    public string? GetLocalStorageSync(string key)
        => ((IJSInProcessRuntime)js).Invoke<string?>("localStorage.getItem", key);

    public void SetLocalStorageSync(string key, string value)
        => ((IJSInProcessRuntime)js).InvokeVoid("localStorage.setItem", key, value);
}
