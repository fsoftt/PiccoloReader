using Microsoft.JSInterop;

namespace PiccoloReader.Web.Services;

// Wrapper over wwwroot/js/reorder.js (drag-to-reorder of sheet rows).
public sealed class ReorderInterop(IJSRuntime js)
{
    private IJSObjectReference? _module;

    public async Task InitAsync<T>(DotNetObjectReference<T> target) where T : class
    {
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/reorder.js");
        await _module.InvokeVoidAsync("init", target);
    }

    public async Task DisposeAsync()
    {
        if (_module is null) return;
        try { await _module.InvokeVoidAsync("dispose"); } catch (JSDisconnectedException) { }
    }
}
