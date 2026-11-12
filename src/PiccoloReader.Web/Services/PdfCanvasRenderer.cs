using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace PiccoloReader.Web.Services;

// Web-specific renderer: Core's IPdfPageRenderer returns PNG bytes, but in the
// browser pdf.js draws straight onto a <canvas>, which avoids the encode/copy.
public sealed class PdfCanvasRenderer(IJSRuntime js)
{
    private readonly Lazy<Task<IJSObjectReference>> _module = new(() =>
        js.InvokeAsync<IJSObjectReference>("import", "./js/pdf.js").AsTask());

    private async Task<IJSObjectReference> ModuleAsync() => await _module.Value;

    // Stores the PDF in OPFS under sheets/<fileName> and returns its page count.
    public async Task<int> ImportAsync(string fileName, byte[] data)
        => await (await ModuleAsync()).InvokeAsync<int>("importPdf", fileName, data);

    public async Task<int> OpenAsync(string fileName)
        => await (await ModuleAsync()).InvokeAsync<int>("openPdf", fileName);

    public async Task RenderPageAsync(string fileName, int pageIndex, ElementReference canvas, int maxWidth, int maxHeight)
        => await (await ModuleAsync()).InvokeVoidAsync("renderPage", fileName, pageIndex, canvas, maxWidth, maxHeight);

    public async Task RegisterKeysAsync(DotNetObjectReference<Pages.Viewer> target)
        => await (await ModuleAsync()).InvokeVoidAsync("registerKeys", target);

    public async Task UnregisterKeysAsync()
        => await (await ModuleAsync()).InvokeVoidAsync("unregisterKeys");
}
