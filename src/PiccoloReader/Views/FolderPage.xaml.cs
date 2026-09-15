using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Views;

[QueryProperty(nameof(FolderId), "folderId")]
[QueryProperty(nameof(FolderName), "folderName")]
public partial class FolderPage : ContentPage
{
    private readonly FolderViewModel _viewModel;

    public FolderPage(FolderViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    public string FolderId
    {
        set => _viewModel.FolderId = int.Parse(value);
    }

    public string FolderName
    {
        set => Title = value;
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

    private async void OnMoveToRootClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: Sheet sheet })
        {
            await _viewModel.MoveSheetCommand.ExecuteAsync((sheet, (int?)null));
        }
    }

    private async void OnDeleteSheetClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: Sheet sheet })
        {
            return;
        }

        var confirmed = await DisplayAlertAsync(
            "Delete sheet",
            $"Delete \"{sheet.Title}\"? This cannot be undone.",
            "Delete",
            "Cancel");

        if (confirmed)
        {
            await _viewModel.DeleteSheetCommand.ExecuteAsync(sheet);
        }
    }
}
