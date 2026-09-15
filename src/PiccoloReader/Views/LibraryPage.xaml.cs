using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Views;

public partial class LibraryPage : ContentPage
{
    private readonly LibraryViewModel _viewModel;

    public LibraryPage(LibraryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnImportPdfClicked(object? sender, EventArgs e)
    {
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Select a PDF",
            FileTypes = FilePickerFileType.Pdf
        });

        if (result is not null)
        {
            await _viewModel.ImportPdfCommand.ExecuteAsync(result.FullPath);
        }
    }

    private async void OnFolderTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is Folder folder)
        {
            await Shell.Current.GoToAsync($"folder?folderId={folder.Id}&folderName={folder.Name}");
        }
    }

    private async void OnDeleteFolderClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: Folder folder })
        {
            return;
        }

        var deleteSheets = await DisplayAlertAsync(
            "Delete folder",
            $"Delete \"{folder.Name}\" and all sheets inside it? Choose \"Keep Sheets\" to move them to the root instead.",
            "Delete Sheets",
            "Keep Sheets");

        if (deleteSheets)
        {
            await _viewModel.DeleteFolderCommand.ExecuteAsync(folder);
        }
        else
        {
            await _viewModel.DeleteFolderKeepSheetsCommand.ExecuteAsync(folder);
        }
    }
}
