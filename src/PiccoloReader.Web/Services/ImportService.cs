using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace PiccoloReader.Web.Services;

// PDFs arriving from outside the file picker: drag-and-drop, the PWA file handler (launch queue)
// and the Web Share Target. pwa.js queues them; they are imported into the folder being viewed.
public sealed partial class ImportService(PwaService pwa, WebLibrary library, UiService ui, NavigationManager nav)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public event Action? Imported;

    public void Start() => pwa.IncomingFiles += () => _ = ProcessAsync();

    public async Task ProcessAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var count = 0;
            while (await pwa.IncomingCountAsync() > 0)
            {
                var name = await pwa.IncomingNameAsync() ?? "shared.pdf";
                var bytes = await pwa.TakeIncomingAsync();
                if (bytes is null) break;
                try
                {
                    if (bytes.Length > WebLibrary.MaxPdfBytes) throw new InvalidDataException("PDF too large");
                    await using var ms = new MemoryStream(bytes, writable: false);
                    await library.ImportAsync(name, ms, CurrentFolderId());
                    count++;
                }
                catch (Exception ex)
                {
                    ui.ShowToast($"{name}: {ex.Message}");
                }
            }
            if (count > 0)
            {
                ui.ShowToast(WebText.Imported(count));
                Imported?.Invoke();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private int? CurrentFolderId()
    {
        var m = FolderRoute().Match(nav.ToBaseRelativePath(nav.Uri));
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    [GeneratedRegex(@"^folder/(\d+)")]
    private static partial Regex FolderRoute();
}
