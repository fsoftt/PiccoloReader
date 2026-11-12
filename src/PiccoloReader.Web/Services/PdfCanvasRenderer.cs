using Microsoft.JSInterop;

namespace PiccoloReader.Web.Services;

// Web-specific PDF access: pdf.js draws straight onto a <canvas> (see viewer.js),
// so Core's PNG-returning IPdfPageRenderer is not used.
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

    // Object URL of the cached first-page thumbnail, or null when the PDF is unreadable.
    public async Task<string?> ThumbnailUrlAsync(string fileName, int sheetId)
        => await (await ModuleAsync()).InvokeAsync<string?>("thumbnailUrl", fileName, sheetId);

    public async Task DeleteFilesAsync(string fileName, int sheetId)
        => await (await ModuleAsync()).InvokeVoidAsync("deleteSheetFiles", fileName, sheetId);
}
