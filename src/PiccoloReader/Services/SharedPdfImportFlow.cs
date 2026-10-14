using CommunityToolkit.Maui.Alerts;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Services;

// PDFs handed to the app from outside (open-with / share): asks where to put
// them (library root or a folder), imports them through the regular import
// path and reports the result. The platform layer copies the shared streams
// to temp files first and calls Enqueue; the batch stays pending until the UI
// shell exists (splash / tutorial may still be showing), so nothing is lost
// or imported twice across configuration changes.
public class SharedPdfImportFlow
{
    private readonly SharedPdfImportService _importService;
    private readonly LibraryService _libraryService;
    private readonly object _lock = new();
    private readonly List<PendingBatch> _pending = new();
    private int _running;

    public SharedPdfImportFlow(SharedPdfImportService importService, LibraryService libraryService)
    {
        _importService = importService;
        _libraryService = libraryService;
    }

    // Raised on the UI thread after sheets were added to the library root.
    public event Action? LibraryChanged;

    // unreadableCount: shared items that could not be copied locally.
    public void Enqueue(IReadOnlyList<string> tempFilePaths, int unreadableCount = 0)
    {
        if (tempFilePaths.Count == 0 && unreadableCount == 0)
        {
            return;
        }

        lock (_lock)
        {
            _pending.Add(new PendingBatch(tempFilePaths, unreadableCount));
        }

        _ = ProcessPendingAsync();
    }

    // Also called by the library page when it appears, in case a batch
    // arrived before the shell was ready.
    public async Task ProcessPendingAsync()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return;
        }

        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                while (Shell.Current?.CurrentPage is { } page && TakeNext() is { } batch)
                {
                    await HandleBatchAsync(page, batch);
                }
            });
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private PendingBatch? TakeNext()
    {
        lock (_lock)
        {
            if (_pending.Count == 0)
            {
                return null;
            }

            var next = _pending[0];
            _pending.RemoveAt(0);
            return next;
        }
    }

    private async Task HandleBatchAsync(Page page, PendingBatch batch)
    {
        if (batch.Paths.Count == 0)
        {
            await Toast.Make(AppStrings.SharedPdfImportFailedMessage).Show();
            return;
        }

        var destination = await ChooseDestinationAsync(page, batch.Paths.Count);
        if (destination is null)
        {
            SharedPdfImportService.Discard(batch.Paths);
            return;
        }

        var result = await _importService.ImportAsync(batch.Paths, destination.Value.Folder?.Id);
        var failed = result.FailedCount + batch.UnreadableCount;

        await Toast.Make(BuildMessage(result, failed)).Show();

        if (result.ImportedCount == 0)
        {
            return;
        }

        if (destination.Value.Folder is { } folder)
        {
            await Shell.Current.GoToAsync($"//library/folder?folderId={folder.Id}&folderName={Uri.EscapeDataString(folder.Name)}");
        }
        else
        {
            await Shell.Current.GoToAsync("//library");
            LibraryChanged?.Invoke();
        }
    }

    private static string BuildMessage(SharedPdfImportResult result, int failed)
    {
        if (result.ImportedCount == 0)
        {
            return AppStrings.SharedPdfImportFailedMessage;
        }

        if (failed > 0)
        {
            return string.Format(AppStrings.SharedPdfImportPartialFormat, result.ImportedCount, failed);
        }

        return result.ImportedCount == 1
            ? string.Format(AppStrings.SharedPdfImportedFormat, result.FirstImportedTitle)
            : string.Format(AppStrings.SharedPdfImportedMultipleFormat, result.ImportedCount);
    }

    // Library root (Folder null), an existing folder, or a new one.
    // Returns null when the user cancels.
    private async Task<(Folder? Folder, bool Chosen)?> ChooseDestinationAsync(Page page, int count)
    {
        var title = count == 1
            ? AppStrings.ImportDestinationTitle
            : string.Format(AppStrings.ImportDestinationTitleMultipleFormat, count);

        while (true)
        {
            var folders = await _libraryService.GetFoldersAsync();
            var options = new List<string> { AppStrings.LibraryRootOption };
            options.AddRange(folders.Select(f => f.Name));
            options.Add(AppStrings.NewFolder);

            var choice = await page.DisplayActionSheetAsync(title, AppStrings.Cancel, null, options.ToArray());

            if (choice is null || choice == AppStrings.Cancel)
            {
                return null;
            }

            if (choice == AppStrings.LibraryRootOption)
            {
                return (null, true);
            }

            if (choice == AppStrings.NewFolder)
            {
                var name = await page.DisplayPromptAsync(AppStrings.CreateFolderTitle, AppStrings.FolderNamePrompt, accept: AppStrings.OK, cancel: AppStrings.Cancel);
                if (name is null)
                {
                    continue;
                }

                var normalized = FolderNameNormalizer.Normalize(name);
                if (normalized is null)
                {
                    await page.DisplayAlertAsync(AppStrings.CreateFolderTitle, AppStrings.FolderNameEmptyMessage, AppStrings.OK);
                    continue;
                }

                var existing = folders.FirstOrDefault(f => string.Equals(f.Name, normalized, StringComparison.OrdinalIgnoreCase));
                return (existing ?? await _libraryService.CreateFolderAsync(normalized), true);
            }

            return (folders.First(f => f.Name == choice), true);
        }
    }

    private sealed record PendingBatch(IReadOnlyList<string> Paths, int UnreadableCount);
}
