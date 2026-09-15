using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.ViewModels;

public partial class SheetViewerViewModel : ObservableObject
{
    private readonly LibraryService _libraryService;
    private readonly IAppStorageProvider _storageProvider;
    private readonly IPdfPageRenderer _pdfPageRenderer;

    private string _filePath = string.Empty;
    private int _targetWidthPx;
    private int _targetHeightPx;

    public SheetViewerViewModel(LibraryService libraryService, IAppStorageProvider storageProvider, IPdfPageRenderer pdfPageRenderer)
    {
        _libraryService = libraryService;
        _storageProvider = storageProvider;
        _pdfPageRenderer = pdfPageRenderer;
    }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyPropertyChangedFor(nameof(PageIndicatorText))]
    private int _pageCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyPropertyChangedFor(nameof(CurrentPageDisplay))]
    [NotifyPropertyChangedFor(nameof(PageIndicatorText))]
    private int _currentPageIndex;

    [ObservableProperty]
    private byte[]? _currentPageImageBytes;

    [ObservableProperty]
    private bool _isLoading;

    public int CurrentPageDisplay => CurrentPageIndex + 1;

    public string PageIndicatorText => $"Page {CurrentPageDisplay} of {PageCount}";

    public async Task LoadAsync(int sheetId, int targetWidthPx, int targetHeightPx)
    {
        IsLoading = true;
        try
        {
            _targetWidthPx = targetWidthPx;
            _targetHeightPx = targetHeightPx;

            var sheet = await _libraryService.GetSheetAsync(sheetId);
            Title = sheet.Title;
            _filePath = Path.Combine(_storageProvider.SheetsDirectory, sheet.FileName);

            var pageCount = sheet.PageCount;
            if (pageCount <= 0)
            {
                pageCount = await _pdfPageRenderer.GetPageCountAsync(_filePath);
                await _libraryService.UpdateSheetPageCountAsync(sheet, pageCount);
            }

            PageCount = pageCount;
            CurrentPageIndex = 0;

            await LoadCurrentPageAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanGoToNextPage() => CurrentPageIndex < PageCount - 1;

    private bool CanGoToPreviousPage() => CurrentPageIndex > 0;

    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    private async Task NextPageAsync()
    {
        CurrentPageIndex++;
        await LoadCurrentPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPage))]
    private async Task PreviousPageAsync()
    {
        CurrentPageIndex--;
        await LoadCurrentPageAsync();
    }

    private async Task LoadCurrentPageAsync()
    {
        CurrentPageImageBytes = await _pdfPageRenderer.RenderPageAsync(_filePath, CurrentPageIndex, _targetWidthPx, _targetHeightPx);
    }
}
