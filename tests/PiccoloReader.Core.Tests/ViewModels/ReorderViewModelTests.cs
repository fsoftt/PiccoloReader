using PiccoloReader.Core.Data;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.Services.Concert;
using PiccoloReader.Core.ViewModels;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.ViewModels;

public class ReorderViewModelTests : IDisposable
{
    private readonly TestAppStorageProvider _storage = new();
    private readonly LibraryService _library;
    private readonly PdfImportService _import;
    private readonly FakeSortPreferenceService _sortPreference = new();
    private readonly ConcertQueue _queue = new();
    private readonly string _tempDir;

    public ReorderViewModelTests()
    {
        Batteries_V2.Init();
        var database = new AppDatabase(_storage.DatabasePath);
        database.InitializeAsync().GetAwaiter().GetResult();
        _library = new LibraryService(database, _storage);
        _import = new PdfImportService(database, _storage);
        _tempDir = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _storage.Dispose();
        Directory.Delete(_tempDir, recursive: true);
    }

    private LibraryViewModel NewLibraryVm() => new(_library, _import, _sortPreference, _queue);

    private FolderViewModel NewFolderVm(int folderId) => new(_library, _import, _sortPreference, _queue) { FolderId = folderId };

    private async Task ImportAsync(string title, int? folderId)
    {
        var path = Path.Combine(_tempDir, $"{title}.pdf");
        await File.WriteAllTextAsync(path, "fake-pdf");
        await _import.ImportAsync(path, folderId);
    }

    private async Task ImportRootAsync(params string[] titles)
    {
        foreach (var title in titles)
        {
            await ImportAsync(title, null);
        }
    }

    [Fact]
    public async Task Library_CommitOrder_SwitchesToCustomPersistsAndKeepsVisibleOrder()
    {
        await ImportRootAsync("a", "b", "c");
        var vm = NewLibraryVm();
        await vm.LoadAsync();
        vm.RootSheets.Move(0, 2); // b, c, a

        await vm.CommitSheetOrderAsync();

        Assert.Equal(SortField.Custom, vm.SheetSortField);
        Assert.Equal(SortField.Custom, _sortPreference.SheetSortField);
        Assert.Equal(new[] { "b", "c", "a" }, (await _library.GetSheetsAsync(null)).Select(s => s.Title));
        Assert.Equal(new[] { "b", "c", "a" }, vm.RootSheets.Select(s => s.Title));
    }

    [Fact]
    public async Task Library_CommitOrder_FromDescendingNameUsesVisibleOrder()
    {
        await ImportRootAsync("a", "b", "c");
        var vm = NewLibraryVm();
        await vm.LoadAsync();
        vm.ApplySheetSort(SortField.Name, SortDirection.Descending); // c, b, a
        vm.RootSheets.Move(2, 0); // a, c, b

        await vm.CommitSheetOrderAsync();

        Assert.Equal(new[] { "a", "c", "b" }, (await _library.GetSheetsAsync(null)).Select(s => s.Title));
    }

    [Fact]
    public void Library_LoadsSavedSortPreference()
    {
        _sortPreference.Save(SortField.DateAdded, SortDirection.Descending);

        var vm = NewLibraryVm();

        Assert.Equal(SortField.DateAdded, vm.SheetSortField);
        Assert.Equal(SortDirection.Descending, vm.SheetSortDirection);
    }

    [Fact]
    public void Library_ApplySheetSort_PersistsChoice()
    {
        var vm = NewLibraryVm();

        vm.ApplySheetSort(SortField.DateAdded, SortDirection.Ascending);

        Assert.Equal(SortField.DateAdded, _sortPreference.SheetSortField);
        Assert.Equal(SortDirection.Ascending, _sortPreference.SheetSortDirection);
    }

    [Fact]
    public async Task Library_CustomSort_UsesStoredOrder()
    {
        await ImportRootAsync("a", "b", "c");
        var ids = (await _library.GetSheetsAsync(null)).Select(s => s.Id).Reverse().ToList();
        await _library.SetSheetOrderAsync(null, ids);
        _sortPreference.Save(SortField.Custom, SortDirection.Ascending);
        var vm = NewLibraryVm();

        await vm.LoadAsync();

        Assert.Equal(new[] { "c", "b", "a" }, vm.RootSheets.Select(s => s.Title));
    }

    [Fact]
    public async Task Library_SearchActive_DisablesReorderAndIgnoresCommit()
    {
        await ImportRootAsync("a", "b");
        var vm = NewLibraryVm();
        await vm.LoadAsync();
        Assert.True(vm.CanReorderSheets);

        vm.SheetSearchText = "a";
        await vm.CommitSheetOrderAsync();

        Assert.False(vm.CanReorderSheets);
        Assert.NotEqual(SortField.Custom, vm.SheetSortField);
    }

    [Fact]
    public async Task Library_PublishConcertQueue_UsesFullListInCurrentSortIgnoringSearch()
    {
        await ImportRootAsync("a", "b", "c");
        var vm = NewLibraryVm();
        await vm.LoadAsync();
        vm.ApplySheetSort(SortField.Name, SortDirection.Descending);
        vm.SheetSearchText = "b";

        vm.PublishConcertQueue();

        var titles = (await _library.GetSheetsAsync(null)).ToDictionary(s => s.Id, s => s.Title);
        Assert.Equal(new[] { "c", "b", "a" }, _queue.SheetIds.Select(id => titles[id]));
    }

    [Fact]
    public async Task Folder_CommitOrderAndQueue_UseFolderSheets()
    {
        var folder = await _library.CreateFolderAsync("F");
        await ImportAsync("root", null);
        foreach (var title in new[] { "x", "y", "z" })
        {
            await ImportAsync(title, folder.Id);
        }

        var vm = NewFolderVm(folder.Id);
        await vm.LoadAsync();
        vm.Sheets.Move(2, 0); // z, x, y
        await vm.CommitSheetOrderAsync();
        vm.PublishConcertQueue();

        var stored = await _library.GetSheetsAsync(folder.Id);
        Assert.Equal(new[] { "z", "x", "y" }, stored.Select(s => s.Title));
        Assert.Equal(stored.Select(s => s.Id), _queue.SheetIds);
        Assert.Equal(SortField.Custom, _sortPreference.SheetSortField);
    }
}
