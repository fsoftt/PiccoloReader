using System.Windows.Input;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Views;

[QueryProperty(nameof(FolderId), "folderId")]
[QueryProperty(nameof(FolderName), "folderName")]
public partial class FolderPage : ContentPage
{
    private readonly FolderViewModel _viewModel;
    private readonly LibraryService _libraryService;

    public FolderPage(FolderViewModel viewModel, LibraryService libraryService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _libraryService = libraryService;
        BindingContext = _viewModel;

        SheetLongPressCommand = new Command<Sheet>(async sheet => await OnSheetLongPressedAsync(sheet));
    }

    public ICommand SheetLongPressCommand { get; }

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
        var results = await FilePicker.Default.PickMultipleAsync(new PickOptions
        {
            PickerTitle = "Select PDFs",
            FileTypes = FilePickerFileType.Pdf
        });

        if (results is null)
        {
            return;
        }

        foreach (var result in results)
        {
            if (result is null)
            {
                continue;
            }

            await _viewModel.ImportPdfCommand.ExecuteAsync(result.FullPath);
        }
    }

    private async void OnSortClicked(object? sender, EventArgs e)
    {
        var choice = await DisplayActionSheetAsync(
            "Sort by",
            "Cancel",
            null,
            "Name (A-Z)",
            "Name (Z-A)",
            "Date Added (Oldest First)",
            "Date Added (Newest First)");

        (SortField Field, SortDirection Direction)? sort = choice switch
        {
            "Name (A-Z)" => (SortField.Name, SortDirection.Ascending),
            "Name (Z-A)" => (SortField.Name, SortDirection.Descending),
            "Date Added (Oldest First)" => (SortField.DateAdded, SortDirection.Ascending),
            "Date Added (Newest First)" => (SortField.DateAdded, SortDirection.Descending),
            _ => null
        };

        if (sort is { } selected)
        {
            _viewModel.ApplySort(selected.Field, selected.Direction);
        }
    }

    private async void OnSheetTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is Sheet sheet)
        {
            await Shell.Current.GoToAsync($"sheetviewer?sheetId={sheet.Id}");
        }
    }

    private async Task OnSheetLongPressedAsync(Sheet sheet)
    {
        var choice = await DisplayActionSheetAsync($"\"{sheet.Title}\"", "Cancel", null, "Move", "Delete");

        if (choice == "Delete")
        {
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
        else if (choice == "Move")
        {
            var folders = await _libraryService.GetFoldersAsync();

            var options = new List<string> { "Root" };
            options.AddRange(folders.Where(f => f.Id != _viewModel.FolderId).Select(f => f.Name));

            var target = await DisplayActionSheetAsync("Move to…", "Cancel", null, options.ToArray());

            if (target is null || target == "Cancel")
            {
                return;
            }

            int? targetFolderId = target == "Root"
                ? null
                : folders.First(f => f.Name == target).Id;

            await _viewModel.MoveSheetCommand.ExecuteAsync((sheet, targetFolderId));
        }
    }
}
