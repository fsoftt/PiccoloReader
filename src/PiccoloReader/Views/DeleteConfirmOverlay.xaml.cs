using PiccoloReader.Core.Resources.Strings;

namespace PiccoloReader.Views;

/// <summary>
/// In-page confirmation for deleting a sheet or folder. Deleting only removes the item from
/// the library; the checkbox (unchecked by default) also deletes the visible copy in storage.
/// </summary>
public partial class DeleteConfirmOverlay : ContentView
{
    public const string StorageRoot = "Documents/PiccoloReader";

    private TaskCompletionSource<(bool Confirmed, bool DeleteFile)>? _pending;

    public DeleteConfirmOverlay()
    {
        InitializeComponent();
    }

    public bool IsShowing => _pending is not null;

    /// <summary>Shows the dialog; completes with (confirmed, deleteFile). Path null/empty hides the path and checkbox.</summary>
    public Task<(bool Confirmed, bool DeleteFile)> ShowAsync(string title, string message, string? displayPath, string checkboxText)
    {
        Cancel();

        TitleLabel.Text = title;
        MessageLabel.Text = message;
        var hasPath = !string.IsNullOrEmpty(displayPath);
        PathLabel.IsVisible = hasPath;
        CheckRow.IsVisible = hasPath;
        PathLabel.Text = hasPath ? string.Format(AppStrings.DeleteLocationFormat, displayPath) : string.Empty;
        CheckLabel.Text = checkboxText;
        DeleteFileCheckBox.IsChecked = false;

        var tcs = new TaskCompletionSource<(bool, bool)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = tcs;
        IsVisible = true;
        return tcs.Task;
    }

    /// <summary>Dismisses the dialog as a cancel (back button, tap outside). No-op when hidden.</summary>
    public void Cancel() => Complete(false, false);

    /// <summary>Documents/PiccoloReader/{externalPath}, or null when the sheet has no external copy yet.</summary>
    public static string? SheetDisplayPath(string? externalPath) =>
        string.IsNullOrWhiteSpace(externalPath) ? null : StorageRoot + "/" + externalPath.Replace((char)92, (char)47).TrimStart((char)47);

    public static string FolderDisplayPath(string folderName) => $"{StorageRoot}/{folderName}/";

    private void Complete(bool confirmed, bool deleteFile)
    {
        var tcs = _pending;
        if (tcs is null)
        {
            return;
        }

        _pending = null;
        IsVisible = false;
        tcs.TrySetResult((confirmed, deleteFile));
    }

    private void OnDeleteClicked(object? sender, EventArgs e) =>
        Complete(true, CheckRow.IsVisible && DeleteFileCheckBox.IsChecked);

    private void OnCancelClicked(object? sender, EventArgs e) => Complete(false, false);

    private void OnBackdropTapped(object? sender, TappedEventArgs e) => Complete(false, false);

    private void OnCardTapped(object? sender, TappedEventArgs e)
    {
    }

    private void OnCheckLabelTapped(object? sender, TappedEventArgs e) =>
        DeleteFileCheckBox.IsChecked = !DeleteFileCheckBox.IsChecked;
}
