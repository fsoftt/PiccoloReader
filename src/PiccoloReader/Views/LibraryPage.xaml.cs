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
        _viewModel.Folders.CollectionChanged += (_, _) => RebuildFolderGrid();

        FolderLongPressCommand = new Command<Folder>(async folder => await OnFolderLongPressedAsync(folder));
        SheetLongPressCommand = new Command<Sheet>(async sheet => await OnSheetLongPressedAsync(sheet));
    }

    private void RebuildFolderGrid()
    {
        FolderGrid.Children.Clear();
        FolderGrid.RowDefinitions.Clear();

        var template = (DataTemplate)Resources["FolderCardTemplate"];
        for (var i = 0; i < _viewModel.Folders.Count; i++)
        {
            if (i % 3 == 0)
            {
                FolderGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }

            var card = (View)template.CreateContent();
            card.BindingContext = _viewModel.Folders[i];
            FolderGrid.Add(card, i % 3, i / 3);
        }
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

    private async void OnCreateFolderClicked(object? sender, EventArgs e)
    {
        var name = await DisplayPromptAsync(AppStrings.CreateFolderTitle, AppStrings.FolderNamePrompt, accept: AppStrings.OK, cancel: AppStrings.Cancel);

        if (name is null)
        {
            return;
        }

        if (FolderNameNormalizer.Normalize(name) is null)
        {
            await DisplayAlertAsync(AppStrings.CreateFolderTitle, AppStrings.FolderNameEmptyMessage, AppStrings.OK);
            return;
        }

        _viewModel.NewFolderName = name;
        await _viewModel.CreateFolderCommand.ExecuteAsync(null);
    }

    private void OnFolderSearchClicked(object? sender, EventArgs e)
    {
        _viewModel.IsFolderSearchVisible = !_viewModel.IsFolderSearchVisible;

        if (!_viewModel.IsFolderSearchVisible)
        {
            _viewModel.FolderSearchText = string.Empty;
        }
    }

    private void OnSheetSearchClicked(object? sender, EventArgs e)
    {
        _viewModel.IsSheetSearchVisible = !_viewModel.IsSheetSearchVisible;

        if (!_viewModel.IsSheetSearchVisible)
        {
            _viewModel.SheetSearchText = string.Empty;
        }
    }

    private void OnFolderSearchClearClicked(object? sender, EventArgs e)
    {
        _viewModel.FolderSearchText = string.Empty;
    }

    private void OnSheetSearchClearClicked(object? sender, EventArgs e)
    {
        _viewModel.SheetSearchText = string.Empty;
    }

    private async void OnFolderSortClicked(object? sender, EventArgs e)
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

    private async void OnSheetSortClicked(object? sender, EventArgs e)
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

    private bool _suppressNextTap;

    private void OnItemTouchStatusChanged(object? sender, CommunityToolkit.Maui.Core.TouchInteractionStatusChangedEventArgs e)
    {
        if (e.TouchInteractionStatus == CommunityToolkit.Maui.Core.TouchInteractionStatus.Started)
        {
            _suppressNextTap = false;
        }
    }

    private bool ConsumeSuppressedTap()
    {
        var suppressed = _suppressNextTap;
        _suppressNextTap = false;
        return suppressed;
    }

    private async void OnFolderTapped(object? sender, TappedEventArgs e)
    {
        if (ConsumeSuppressedTap())
        {
            return;
        }

        if (e.Parameter is Folder folder)
        {
            await Shell.Current.GoToAsync($"folder?folderId={folder.Id}&folderName={folder.Name}");
        }
    }

    private async void OnSheetTapped(object? sender, TappedEventArgs e)
    {
        if (ConsumeSuppressedTap())
        {
            return;
        }

        if (e.Parameter is Sheet sheet)
        {
            await Shell.Current.GoToAsync($"sheetviewer?sheetId={sheet.Id}");
        }
    }

    private async Task OnFolderLongPressedAsync(Folder folder)
    {
        _suppressNextTap = true;

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

    private async void OnSheetMoreClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is Sheet sheet)
        {
            await ShowSheetMenuAsync(sheet);
        }
    }

    private async Task OnSheetLongPressedAsync(Sheet sheet)
    {
        _suppressNextTap = true;
        await ShowSheetMenuAsync(sheet);
    }

    private async Task ShowSheetMenuAsync(Sheet sheet)
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
