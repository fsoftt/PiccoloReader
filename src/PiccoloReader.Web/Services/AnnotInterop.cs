using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace PiccoloReader.Web.Services;

// Wrapper over wwwroot/js/annot.js (annotation painting, pointer input, annotated PDF export).
public sealed class AnnotInterop(IJSRuntime js)
{
    private readonly Lazy<Task<IJSObjectReference>> _module = new(() =>
        js.InvokeAsync<IJSObjectReference>("import", "./js/annot.js").AsTask());

    private async Task<IJSObjectReference> M() => await _module.Value;

    public async Task AttachAsync<T>(DotNetObjectReference<T> self, ElementReference stage) where T : class
        => await (await M()).InvokeVoidAsync("attachInput", self, stage);

    public async Task DetachAsync() => await (await M()).InvokeVoidAsync("detachInput");

    public async Task SetOptionsAsync(string tool, bool editing, bool stylusDirect, string pencilColor, double pencilW, string hlColor, double hlW, double eraserR)
        => await (await M()).InvokeVoidAsync("setOptions", tool, editing, stylusDirect, pencilColor, pencilW, hlColor, hlW, eraserR);

    public async Task SetPageAsync(int page, string json) => await (await M()).InvokeVoidAsync("setPage", page, json);

    public async Task SelectAsync(int page, int id) => await (await M()).InvokeVoidAsync("selectById", page, id);

    public async Task<double[]?> VisibleAreaAsync(int page) => await (await M()).InvokeAsync<double[]?>("visibleArea", page);

    public async Task<double> TextAspectAsync(string text) => await (await M()).InvokeAsync<double>("textAspect", text);

    public async Task PaintTilesAsync(ElementReference container, string specsJson)
    {
        var m = await M();
        await m.InvokeVoidAsync("paintTilesJson", container, specsJson);
    }

    public async Task BindSheetHandleAsync<T>(ElementReference handle, DotNetObjectReference<T> self) where T : class
        => await (await M()).InvokeVoidAsync("bindSheetHandle", handle, self);

    public async Task ExportBeginAsync() => await (await M()).InvokeVoidAsync("exportBegin");

    public async Task ExportPageAsync(string name, int page, string json) => await (await M()).InvokeVoidAsync("exportPage", name, page, json);

    public async Task<string> ExportFinishAsync(string fileName, bool share) => await (await M()).InvokeAsync<string>("exportFinish", fileName, share);

    public async Task<string> ExportOriginalAsync(string name, string fileName, bool share) => await (await M()).InvokeAsync<string>("exportOriginal", name, fileName, share);

    public async Task<string> ShareLastAsync() => await (await M()).InvokeAsync<string>("shareLast");
}
