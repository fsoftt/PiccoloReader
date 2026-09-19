using System.Windows.Input;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Resources.Strings;
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

    private async void OnImportPdfClicked(object? sender, TappedEventArgs e)
    {
        var results = await FilePicker.Default.PickMultipleAsync(new PickOptions
        {
            PickerTitle = AppStrings.SelectPdfsPickerTitle,
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

    private async void OnCreateFolderClicked(object? sender, TappedEventArgs e)
    {
        var name = await DisplayPromptAsync(AppStrings.CreateFolderTitle, AppStrings.FolderNamePrompt, accept: AppStrings.OK, cancel: AppStrings.Cancel);

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

    private void OnFolderSearchClearClicked(object? sender, TappedEventArgs e)
    {
        _viewModel.FolderSearchText = string.Empty;
    }

    private void OnSheetSearchClearClicked(object? sender, TappedEventArgs e)
    {
        _viewModel.SheetSearchText = string.Empty;
    }

    private async void OnFolderSortClicked(object? sender, TappedEventArgs e)
    {
        var choice = await DisplayActionSheetAsync(
            AppStrings.SortFoldersByTitle,
            AppStrings.Cancel,
            null,
            AppStrings.SortNameAscending,
            AppStrings.SortNameDescending,
            AppStrings.SortDateAddedAscending,
            AppStrings.SortDateAddedDescending);

        (SortField Field, SortDirection Direction)? sort = choice switch
        {
            var c when c == AppStrings.SortNameAscending => (SortField.Name, SortDirection.Ascending),
            var c when c == AppStrings.SortNameDescending => (SortField.Name, SortDirection.Descending),
            var c when c == AppStrings.SortDateAddedAscending => (SortField.DateAdded, SortDirection.Ascending),
            var c when c == AppStrings.SortDateAddedDescending => (SortField.DateAdded, SortDirection.Descending),
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
            AppStrings.SortSheetsByTitle,
            AppStrings.Cancel,
            null,
            AppStrings.SortNameAscending,
            AppStrings.SortNameDescending,
            AppStrings.SortDateAddedAscending,
            AppStrings.SortDateAddedDescending);

        (SortField Field, SortDirection Direction)? sort = choice switch
        {
            var c when c == AppStrings.SortNameAscending => (SortField.Name, SortDirection.Ascending),
            var c when c == AppStrings.SortNameDescending => (SortField.Name, SortDirection.Descending),
            var c when c == AppStrings.SortDateAddedAscending => (SortField.DateAdded, SortDirection.Ascending),
            var c when c == AppStrings.SortDateAddedDescending => (SortField.DateAdded, SortDirection.Descending),
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
        var choice = await DisplayActionSheetAsync($"\"{folder.Name}\"", AppStrings.Cancel, null, AppStrings.DeleteSheetsOption, AppStrings.KeepSheetsOption);

        if (choice == AppStrings.DeleteSheetsOption)
        {
            await _viewModel.DeleteFolderCommand.ExecuteAsync(folder);
        }
        else if (choice == AppStrings.KeepSheetsOption)
        {
            await _viewModel.DeleteFolderKeepSheetsCommand.ExecuteAsync(folder);
        }
    }

    private async Task OnSheetLongPressedAsync(Sheet sheet)
    {
        var choice = await DisplayActionSheetAsync($"\"{sheet.Title}\"", AppStrings.Cancel, null, AppStrings.Move, AppStrings.Delete);

        if (choice == AppStrings.Delete)
        {
            var confirmed = await DisplayAlertAsync(
                AppStrings.DeleteSheetTitle,
                string.Format(AppStrings.DeleteSheetMessageFormat, sheet.Title),
                AppStrings.Delete,
                AppStrings.Cancel);

            if (confirmed)
            {
                await _viewModel.DeleteSheetCommand.ExecuteAsync(sheet);
            }
        }
        else if (choice == AppStrings.Move)
        {
            var folders = await _libraryService.GetFoldersAsync();

            if (folders.Count == 0)
            {
                await DisplayAlertAsync(AppStrings.Move, AppStrings.NoFoldersToMoveMessage, AppStrings.OK);
                return;
            }

            var target = await DisplayActionSheetAsync(AppStrings.MoveToTitle, AppStrings.Cancel, null, folders.Select(f => f.Name).ToArray());

            if (target is null || target == AppStrings.Cancel)
            {
                return;
            }

            var targetFolder = folders.First(f => f.Name == target);
            await _viewModel.MoveSheetCommand.ExecuteAsync((sheet, (int?)targetFolder.Id));
        }
    }
}
