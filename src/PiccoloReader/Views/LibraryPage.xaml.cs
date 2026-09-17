using System.Windows.Input;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Views;

public partial class LibraryPage : ContentPage
{
    private readonly LibraryViewModel _viewModel;
    private readonly LibraryService _libraryService;

    public LibraryPage(LibraryViewModel viewModel, LibraryService libraryService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _libraryService = libraryService;
        BindingContext = _viewModel;

        FolderLongPressCommand = new Command<Folder>(async folder => await OnFolderLongPressedAsync(folder));
        SheetLongPressCommand = new Command<Sheet>(async sheet => await OnSheetLongPressedAsync(sheet));
    }

    public ICommand FolderLongPressCommand { get; }

    public ICommand SheetLongPressCommand { get; }

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

    private async void OnCreateFolderClicked(object? sender, EventArgs e)
    {
        var name = await DisplayPromptAsync("Create Folder", "Folder name:");

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _viewModel.NewFolderName = name;
        await _viewModel.CreateFolderCommand.ExecuteAsync(null);
    }

    private void OnFolderSearchClicked(object? sender, TappedEventArgs e)
    {
        _viewModel.IsFolderSearchVisible = !_viewModel.IsFolderSearchVisible;

        if (!_viewModel.IsFolderSearchVisible)
        {
            _viewModel.FolderSearchText = string.Empty;
        }
    }

    private void OnSheetSearchClicked(object? sender, TappedEventArgs e)
    {
        _viewModel.IsSheetSearchVisible = !_viewModel.IsSheetSearchVisible;

        if (!_viewModel.IsSheetSearchVisible)
        {
            _viewModel.SheetSearchText = string.Empty;
        }
    }

    private async void OnFolderSortClicked(object? sender, TappedEventArgs e)
    {
        var choice = await DisplayActionSheetAsync(
            "Sort folders by",
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
            _viewModel.ApplyFolderSort(selected.Field, selected.Direction);
        }
    }

    private async void OnSheetSortClicked(object? sender, TappedEventArgs e)
    {
        var choice = await DisplayActionSheetAsync(
            "Sort sheets by",
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
            _viewModel.ApplySheetSort(selected.Field, selected.Direction);
        }
    }

    private async void OnFolderTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is Folder folder)
        {
            await Shell.Current.GoToAsync($"folder?folderId={folder.Id}&folderName={folder.Name}");
        }
    }

    private async void OnSheetTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is Sheet sheet)
        {
            await Shell.Current.GoToAsync($"sheetviewer?sheetId={sheet.Id}");
        }
    }

    private async Task OnFolderLongPressedAsync(Folder folder)
    {
        var choice = await DisplayActionSheetAsync($"\"{folder.Name}\"", "Cancel", null, "Delete Sheets", "Keep Sheets");

        switch (choice)
        {
            case "Delete Sheets":
                await _viewModel.DeleteFolderCommand.ExecuteAsync(folder);
                break;
            case "Keep Sheets":
                await _viewModel.DeleteFolderKeepSheetsCommand.ExecuteAsync(folder);
                break;
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

            if (folders.Count == 0)
            {
                await DisplayAlertAsync("Move", "Create a folder first to move sheets into.", "OK");
                return;
            }

            var target = await DisplayActionSheetAsync("Move to…", "Cancel", null, folders.Select(f => f.Name).ToArray());

            if (target is null || target == "Cancel")
            {
                return;
            }

            var targetFolder = folders.First(f => f.Name == target);
            await _viewModel.MoveSheetCommand.ExecuteAsync((sheet, (int?)targetFolder.Id));
        }
    }
}
