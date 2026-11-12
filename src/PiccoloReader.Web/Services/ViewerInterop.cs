using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace PiccoloReader.Web.Services;

// Wrapper over wwwroot/js/viewer.js (page layout, input, continuous list, crop editor).
public sealed class ViewerInterop(IJSRuntime js)
{
    private readonly Lazy<Task<IJSObjectReference>> _module = new(() =>
        js.InvokeAsync<IJSObjectReference>("import", "./js/viewer.js").AsTask());

    private async Task<IJSObjectReference> M() => await _module.Value;

    public async Task<double[]> PageSizesAsync(string name) => await (await M()).InvokeAsync<double[]>("pageSizes", name);

    public async Task RenderPagedAsync(string name, int i, ElementReference canvas, ElementReference stage, double zoom, double[]? crop)
        => await (await M()).InvokeVoidAsync("renderPaged", name, i, canvas, stage, zoom, crop);

    public async Task RenderFitAsync(string name, int i, ElementReference canvas, ElementReference box)
        => await (await M()).InvokeVoidAsync("renderFit", name, i, canvas, box);

    public async Task AttachAsync<T>(DotNetObjectReference<T> self, ElementReference stage, string mode) where T : class
        => await (await M()).InvokeVoidAsync("attach", self, stage, mode);

    public async Task SetModeAsync(string mode) => await (await M()).InvokeVoidAsync("setMode", mode);

    public async Task DetachAsync() => await (await M()).InvokeVoidAsync("detach");

    public async Task<string> StepAsync(ElementReference stage, int dir) => await (await M()).InvokeAsync<string>("step", stage, dir);

    public async Task<string> ScrollStepAsync(ElementReference stage, int dir) => await (await M()).InvokeAsync<string>("scrollStep", stage, dir);

    public async Task ResetScrollAsync(ElementReference stage, bool toBottom) => await (await M()).InvokeVoidAsync("resetScroll", stage, toBottom);

    public async Task ObserveContinuousAsync<T>(ElementReference stage, DotNetObjectReference<T> self, string name) where T : class
        => await (await M()).InvokeVoidAsync("observeContinuous", stage, self, name);

    public async Task SetZoomAsync(ElementReference stage, double zoom) => await (await M()).InvokeVoidAsync("setZoom", stage, zoom);

    public async Task ScrollToPageAsync(ElementReference stage, int i) => await (await M()).InvokeVoidAsync("scrollToPage", stage, i);

    public async Task CropStartAsync(ElementReference layer, double[] rect) => await (await M()).InvokeVoidAsync("cropStart", layer, rect);

    public async Task<double[]?> CropGetAsync() => await (await M()).InvokeAsync<double[]?>("cropGet");

    public async Task CropResetAsync() => await (await M()).InvokeVoidAsync("cropReset");

    public async Task CropStopAsync() => await (await M()).InvokeVoidAsync("cropStop");
}
