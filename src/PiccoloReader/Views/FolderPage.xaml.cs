using System.ComponentModel;
using System.Windows.Input;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Resources.Strings;
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
        var folderExists = await _viewModel.LoadAsync();

        if (!folderExists)
        {
            // The folder was deleted: back to the library.
            await Shell.Current.GoToAsync("..");
        }
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

    private void OnSearchClearClicked(object? sender, EventArgs e)
    {
        _viewModel.SearchText = string.Empty;
    }

    private async void OnSortClicked(object? sender, EventArgs e)
    {
        var choice = await DisplayActionSheetAsync(
            AppStrings.SortByTitle,
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
            _viewModel.ApplySort(selected.Field, selected.Direction);
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

    private async void OnBackClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");

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

            var options = new List<string> { AppStrings.RootOption };
            options.AddRange(folders.Where(f => f.Id != _viewModel.FolderId).Select(f => f.Name));

            var target = await DisplayActionSheetAsync(AppStrings.MoveToTitle, AppStrings.Cancel, null, options.ToArray());

            if (target is null || target == AppStrings.Cancel)
            {
                return;
            }

            int? targetFolderId = target == AppStrings.RootOption
                ? null
                : folders.First(f => f.Name == target).Id;

            await _viewModel.MoveSheetCommand.ExecuteAsync((sheet, targetFolderId));
        }
    }
}
