using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.Services.ExternalLibrary;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.Services.ExternalLibrary;

// One "installation": private storage + database + services wired to a store.
internal sealed class AppInstall : IDisposable
{
    public TestAppStorageProvider Storage { get; } = new();

    public AppDatabase Database { get; }

    public FakeExternalLibraryState State { get; } = new();

    public ExternalLibrarySync Sync { get; }

    public LibraryService Library { get; }

    public PdfImportService Import { get; }

    public AnnotationService Annotations { get; }

    public BookmarkService Bookmarks { get; }

    public PageCropService Crops { get; }

    public LibraryRecovery Recovery { get; }

    public AppInstall(IExternalLibraryStore store)
    {
        Batteries_V2.Init();
        Database = new AppDatabase(Storage.DatabasePath);
        Database.InitializeAsync().GetAwaiter().GetResult();
        Sync = new ExternalLibrarySync(Database, Storage, store, State, TimeSpan.Zero);
        Library = new LibraryService(Database, Storage, Sync);
        Import = new PdfImportService(Database, Storage, Sync);
        Annotations = new AnnotationService(Database, Sync);
        Bookmarks = new BookmarkService(Database, Sync);
        Crops = new PageCropService(Database, Sync);
        Recovery = new LibraryRecovery(Database, Storage);
    }

    public async Task<Sheet> ImportPdfAsync(string title, string content, int? folderId = null)
    {
        var source = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-src");
        Directory.CreateDirectory(source);
        var path = Path.Combine(source, title + ".pdf");
        await File.WriteAllTextAsync(path, content);
        try
        {
            return await Import.ImportAsync(path, folderId);
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    // Snapshot with the volatile timestamp removed, for equality checks.
    public async Task<string> NormalizedSnapshotAsync()
    {
        var snapshot = await Sync.BuildSnapshotAsync();
        snapshot.CreatedUtc = default;
        // Sheet ExternalPath/hash are part of the library state too.
        return snapshot.ToJson();
    }

    public void Dispose()
    {
        Sync.WaitForPendingAsync().GetAwaiter().GetResult();
        Storage.Dispose();
    }
}

public class ExternalLibraryTests
{
    private readonly FakeExternalLibraryStore _store = new();

    [Fact]
    public async Task Import_CopiesPdfToFolderDirectoryAndRecordsHash()
    {
        using var app = new AppInstall(_store);
        var folder = await app.Library.CreateFolderAsync("Jazz");

        var sheet = await app.ImportPdfAsync("Blue Bossa", "pdf-1", folder.Id);

        Assert.Equal("Jazz/Blue Bossa.pdf", sheet.ExternalPath);
        Assert.Equal("pdf-1", System.Text.Encoding.UTF8.GetString(_store.Files["Jazz/Blue Bossa.pdf"]));
        Assert.Equal(64, sheet.ContentHash!.Length);
    }

    [Fact]
    public async Task Import_SameTitleTwice_GetsNumberedName()
    {
        using var app = new AppInstall(_store);

        var a = await app.ImportPdfAsync("Etude", "a");
        var b = await app.ImportPdfAsync("Etude", "b");

        Assert.Equal("Etude.pdf", a.ExternalPath);
        Assert.Equal("Etude (2).pdf", b.ExternalPath);
    }

    [Fact]
    public async Task Import_WhenStoreDenied_StillImportsAndStaysPending()
    {
        _store.AccessGranted = false;
        using var app = new AppInstall(_store);

        var sheet = await app.ImportPdfAsync("Etude", "a");

        Assert.Null(sheet.ExternalPath);
        Assert.True(File.Exists(Path.Combine(app.Storage.SheetsDirectory, sheet.FileName)));
        Assert.Equal(1, await app.Sync.CountPendingAsync());
    }

    [Fact]
    public async Task MoveSheet_MovesExternalFileToNewFolderDirectory()
    {
        using var app = new AppInstall(_store);
        var folder = await app.Library.CreateFolderAsync("Rock");
        var sheet = await app.ImportPdfAsync("Song", "s");

        await app.Library.MoveSheetAsync(sheet, folder.Id);

        Assert.False(_store.Files.ContainsKey("Song.pdf"));
        Assert.True(_store.Files.ContainsKey("Rock/Song.pdf"));
        Assert.Equal("Rock/Song.pdf", (await app.Library.GetSheetAsync(sheet.Id)).ExternalPath);
    }

    [Fact]
    public async Task MoveSheet_WhenStoreCannotMove_RewritesFromLocalCache()
    {
        _store.MoveSupported = false;
        using var app = new AppInstall(_store);
        var folder = await app.Library.CreateFolderAsync("Rock");
        var sheet = await app.ImportPdfAsync("Song", "s");

        await app.Library.MoveSheetAsync(sheet, folder.Id);

        Assert.False(_store.Files.ContainsKey("Song.pdf"));
        Assert.Equal("s", System.Text.Encoding.UTF8.GetString(_store.Files["Rock/Song.pdf"]));
    }

    [Fact]
    public async Task MoveSheet_WithStaleSheetInstance_DoesNotCopyTwice()
    {
        using var app = new AppInstall(_store);
        var folder = await app.Library.CreateFolderAsync("Rock");
        var sheet = await app.ImportPdfAsync("Song", "s");
        var stale = new Sheet { Id = sheet.Id, FileName = sheet.FileName, Title = sheet.Title };

        await app.Library.MoveSheetAsync(stale, folder.Id);

        Assert.Single(_store.Files.Keys.Where(k => k.EndsWith(".pdf")));
    }

    [Fact]
    public async Task DeleteSheet_RemovesExternalFile()
    {
        using var app = new AppInstall(_store);
        var sheet = await app.ImportPdfAsync("Song", "s");

        await app.Library.DeleteSheetAsync(sheet);

        Assert.False(_store.Files.ContainsKey("Song.pdf"));
    }

    [Fact]
    public async Task DeleteFolder_WithSheets_RemovesTheirFiles()
    {
        using var app = new AppInstall(_store);
        var folder = await app.Library.CreateFolderAsync("Rock");
        await app.ImportPdfAsync("Song", "s", folder.Id);

        await app.Library.DeleteFolderAsync(folder.Id, deleteSheets: true);

        Assert.DoesNotContain(_store.Files.Keys, k => k.EndsWith(".pdf"));
    }

    [Fact]
    public async Task DeleteFolder_KeepingSheets_MovesFilesToRoot()
    {
        using var app = new AppInstall(_store);
        var folder = await app.Library.CreateFolderAsync("Rock");
        await app.ImportPdfAsync("Song", "s", folder.Id);

        await app.Library.DeleteFolderAsync(folder.Id, deleteSheets: false);

        Assert.True(_store.Files.ContainsKey("Song.pdf"));
        Assert.False(_store.Files.ContainsKey("Rock/Song.pdf"));
    }

    [Fact]
    public async Task Changes_AreDebouncedIntoOneSnapshotWrite()
    {
        using var app = new AppInstall(_store);
        var sheet = await app.ImportPdfAsync("Song", "s");
        await app.Sync.WaitForPendingAsync();
        _store.WrittenPaths.Clear();

        await app.Annotations.AddIconAsync(sheet.Id, 0, "note", 0.1, 0.1, 0.1, 0.1);
        await app.Bookmarks.AddBookmarkAsync(sheet.Id, 2, "Coda");
        await app.Sync.WaitForPendingAsync();

        var json = _store.SnapshotJson();
        Assert.NotNull(json);
        var snapshot = LibrarySnapshot.TryParse(json)!;
        Assert.Single(snapshot.Annotations);
        Assert.Single(snapshot.Bookmarks);
        Assert.NotNull(app.State.LastSyncUtc);
    }

    [Fact]
    public async Task Snapshot_RoundTripsThroughJson()
    {
        using var app = new AppInstall(_store);
        var folder = await app.Library.CreateFolderAsync("Jazz");
        var sheet = await app.ImportPdfAsync("Song", "s", folder.Id);
        await app.Annotations.AddStrokeAsync(sheet.Id, 1, "#ff0000", 0.01,
            new[] { new StrokePoint(0.1, 0.2), new StrokePoint(0.3, 0.4) });
        await app.Crops.SetCropAsync(sheet.Id, 0, new PiccoloReader.Core.ViewModels.PageCrop(0.1, 0.1, 0.9, 0.9));

        var original = await app.Sync.BuildSnapshotAsync();
        var parsed = LibrarySnapshot.TryParse(original.ToJson())!;

        Assert.Equal(original.ToJson(), parsed.ToJson());
        Assert.Equal("Jazz", parsed.Sheets.Single().FolderName);
        Assert.Equal(sheet.FileName, parsed.Annotations.Single().SheetKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{not json")]
    public void TryParse_InvalidContent_ReturnsNull(string? json) =>
        Assert.Null(LibrarySnapshot.TryParse(json));

    [Fact]
    public async Task Recovery_AfterReinstall_RebuildsIdenticalLibrary()
    {
        string before;
        string keyOfSong;
        using (var old = new AppInstall(_store))
        {
            var jazz = await old.Library.CreateFolderAsync("Jazz");
            await old.Library.CreateFolderAsync("Empty");
            var song = await old.ImportPdfAsync("Song", "song-bytes", jazz.Id);
            var other = await old.ImportPdfAsync("Loose", "loose-bytes");
            keyOfSong = song.FileName;
            await old.Library.UpdateSheetPageCountAsync(song, 4);
            await old.Library.UpdateSheetLastViewedPageAsync(song, 2);
            await old.Annotations.AddIconAsync(song.Id, 0, "forte", 0.2, 0.3, 0.1, 0.1);
            await old.Annotations.AddStrokeAsync(song.Id, 1, "#00ff00", 0.02, new[] { new StrokePoint(0.5, 0.5), new StrokePoint(0.6, 0.6) });
            await old.Bookmarks.AddBookmarkAsync(other.Id, 0, "Start");
            await old.Crops.SetCropAsync(song.Id, 3, new PiccoloReader.Core.ViewModels.PageCrop(0.05, 0.1, 0.95, 0.9));
            await old.Sync.WaitForPendingAsync();
            before = await old.NormalizedSnapshotAsync();
        }

        // Reinstall: private storage and database are gone, the folder remains.
        using var fresh = new AppInstall(_store);
        Assert.Empty(await fresh.Library.GetFoldersAsync());

        var result = await fresh.Recovery.RecoverAsync(_store);
        await fresh.Sync.SyncAllAsync();

        Assert.True(result.SnapshotFound);
        Assert.Equal(2, result.SheetsRestored);
        Assert.Equal(0, result.OrphansImported);
        Assert.Equal(2, result.AnnotationsAdded);
        Assert.Equal(1, result.BookmarksAdded);
        Assert.Equal(1, result.CropsAdded);
        Assert.Equal(before, await fresh.NormalizedSnapshotAsync());

        // GUIDs kept, PDF cache refilled with the real bytes.
        var songPath = Path.Combine(fresh.Storage.SheetsDirectory, keyOfSong);
        Assert.Equal("song-bytes", await File.ReadAllTextAsync(songPath));
    }

    [Fact]
    public async Task Recovery_RunTwice_AddsNothingTheSecondTime()
    {
        using (var old = new AppInstall(_store))
        {
            var folder = await old.Library.CreateFolderAsync("Jazz");
            var song = await old.ImportPdfAsync("Song", "s", folder.Id);
            await old.Annotations.AddIconAsync(song.Id, 0, "forte", 0.2, 0.3, 0.1, 0.1);
            await old.Bookmarks.AddBookmarkAsync(song.Id, 0, "A");
            await old.Crops.SetCropAsync(song.Id, 0, new PiccoloReader.Core.ViewModels.PageCrop(0.1, 0.1, 0.9, 0.9));
            await old.Sync.WaitForPendingAsync();
        }

        using var fresh = new AppInstall(_store);
        await fresh.Recovery.RecoverAsync(_store);
        var firstRun = await fresh.NormalizedSnapshotAsync();

        var second = await fresh.Recovery.RecoverAsync(_store);

        Assert.Equal(0, second.SheetsRestored);
        Assert.Equal(0, second.OrphansImported);
        Assert.Equal(0, second.AnnotationsAdded);
        Assert.Equal(0, second.BookmarksAdded);
        Assert.Equal(0, second.CropsAdded);
        Assert.Equal(0, second.FoldersAdded);
        Assert.Equal(1, second.SheetsAlreadyPresent);
        Assert.Equal(firstRun, await fresh.NormalizedSnapshotAsync());
    }

    [Fact]
    public async Task Recovery_MissingPdf_IsReportedAndItsDataSkipped()
    {
        using (var old = new AppInstall(_store))
        {
            var keep = await old.ImportPdfAsync("Keep", "k");
            var lost = await old.ImportPdfAsync("Lost", "l");
            await old.Annotations.AddIconAsync(keep.Id, 0, "forte", 0.2, 0.3, 0.1, 0.1);
            await old.Annotations.AddIconAsync(lost.Id, 0, "piano", 0.2, 0.3, 0.1, 0.1);
            await old.Sync.WaitForPendingAsync();
        }

        _store.Files.Remove("Lost.pdf");

        using var fresh = new AppInstall(_store);
        var result = await fresh.Recovery.RecoverAsync(_store);

        Assert.Equal(1, result.SheetsRestored);
        Assert.Equal(new[] { "Lost" }, result.MissingSheets);
        Assert.Equal(1, result.AnnotationsAdded);
    }

    [Fact]
    public async Task Recovery_RenamedOrMovedPdf_IsRelinkedByHash()
    {
        string key;
        using (var old = new AppInstall(_store))
        {
            var song = await old.ImportPdfAsync("Song", "unique-content");
            key = song.FileName;
            await old.Annotations.AddIconAsync(song.Id, 0, "forte", 0.2, 0.3, 0.1, 0.1);
            await old.Sync.WaitForPendingAsync();
        }

        // The user reorganised the folder by hand.
        _store.Files["Archive/Renamed.pdf"] = _store.Files["Song.pdf"];
        _store.Files.Remove("Song.pdf");

        using var fresh = new AppInstall(_store);
        var result = await fresh.Recovery.RecoverAsync(_store);

        Assert.Equal(1, result.SheetsRestored);
        Assert.Equal(0, result.OrphansImported);
        Assert.Equal(1, result.AnnotationsAdded);
        var sheet = (await fresh.Library.GetSheetsAsync(null)).Single();
        Assert.Equal(key, sheet.FileName);
        Assert.Equal("Archive/Renamed.pdf", sheet.ExternalPath);
    }

    [Fact]
    public async Task Recovery_WithoutSnapshot_ImportsPdfsUsingDirectoriesAsFolders()
    {
        _store.Files["Jazz/Blue.pdf"] = System.Text.Encoding.UTF8.GetBytes("blue");
        _store.Files["Loose.pdf"] = System.Text.Encoding.UTF8.GetBytes("loose");
        _store.Files["notes.txt"] = System.Text.Encoding.UTF8.GetBytes("ignored");

        using var app = new AppInstall(_store);
        var result = await app.Recovery.RecoverAsync(_store);
        var again = await app.Recovery.RecoverAsync(_store);

        Assert.False(result.SnapshotFound);
        Assert.Equal(2, result.OrphansImported);
        Assert.Equal(0, again.OrphansImported);
        var folder = Assert.Single(await app.Library.GetFoldersAsync());
        Assert.Equal("Jazz", folder.Name);
        Assert.Equal("Blue", (await app.Library.GetSheetsAsync(folder.Id)).Single().Title);
        Assert.Equal("Loose", (await app.Library.GetSheetsAsync(null)).Single().Title);
    }

    [Fact]
    public async Task Recovery_CorruptSnapshot_FallsBackToPdfImport()
    {
        _store.Files[ExternalPaths.SnapshotFileName] = System.Text.Encoding.UTF8.GetBytes("{broken");
        _store.Files["A.pdf"] = System.Text.Encoding.UTF8.GetBytes("a");

        using var app = new AppInstall(_store);
        var result = await app.Recovery.RecoverAsync(_store);

        Assert.False(result.SnapshotFound);
        Assert.Equal(1, result.OrphansImported);
    }

    [Fact]
    public async Task Migration_CopiesExistingSheets_AndIsIdempotent()
    {
        // A 1.1.0 install: sheets exist locally with no external copy yet.
        _store.AccessGranted = false;
        using var app = new AppInstall(_store);
        var folder = await app.Library.CreateFolderAsync("Jazz");
        await app.ImportPdfAsync("One", "1", folder.Id);
        await app.ImportPdfAsync("Two", "2");
        Assert.Equal(2, await app.Sync.CountPendingAsync());

        _store.AccessGranted = true;
        var progress = new List<SyncProgress>();
        var first = await app.Sync.SyncAllAsync(new Progress<SyncProgress>(progress.Add));

        Assert.Equal(2, first.Synced);
        Assert.Equal(0, first.Failed);
        Assert.True(first.SnapshotWritten);
        Assert.Equal(0, await app.Sync.CountPendingAsync());
        Assert.True(_store.Files.ContainsKey("Jazz/One.pdf"));
        Assert.True(_store.Files.ContainsKey("Two.pdf"));
        Assert.NotNull(app.State.LastSyncUtc);

        var pdfWrites = _store.WrittenPaths.Count(p => p.EndsWith(".pdf"));
        await app.Sync.SyncAllAsync();
        Assert.Equal(pdfWrites, _store.WrittenPaths.Count(p => p.EndsWith(".pdf")));
    }

    [Fact]
    public async Task Migration_WhenAccessDenied_ReportsAndKeepsEverythingPending()
    {
        using var app = new AppInstall(new FakeExternalLibraryStore { AccessGranted = false });
        await app.ImportPdfAsync("One", "1");

        var result = await app.Sync.SyncAllAsync();

        Assert.True(result.AccessDenied);
        Assert.Equal(1, await app.Sync.CountPendingAsync());
        Assert.True(File.Exists(Path.Combine(app.Storage.SheetsDirectory, (await app.Library.GetSheetsAsync(null)).Single().FileName)));
    }

    [Fact]
    public async Task Migration_MissingLocalPdf_CountsAsFailedAndRetries()
    {
        using var app = new AppInstall(_store);
        _store.AccessGranted = false;
        var sheet = await app.ImportPdfAsync("One", "1");
        _store.AccessGranted = true;
        var local = Path.Combine(app.Storage.SheetsDirectory, sheet.FileName);
        var bytes = await File.ReadAllBytesAsync(local);
        File.Delete(local);

        var failed = await app.Sync.SyncAllAsync();
        Assert.Equal(1, failed.Failed);

        await File.WriteAllBytesAsync(local, bytes);
        var retried = await app.Sync.SyncAllAsync();
        Assert.Equal(0, retried.Failed);
        Assert.True(_store.Files.ContainsKey("One.pdf"));
    }

    [Fact]
    public async Task Sync_RecreatesExternalFileDeletedByTheUser()
    {
        using var app = new AppInstall(_store);
        await app.ImportPdfAsync("One", "1");
        _store.Files.Remove("One.pdf");

        await app.Sync.SyncAllAsync();

        Assert.True(_store.Files.ContainsKey("One.pdf"));
    }

    [Fact]
    public async Task Database_BackfillsSyncIdsOfRowsFromBeforeTheColumn()
    {
        using var app = new AppInstall(_store);
        var sheet = await app.ImportPdfAsync("One", "1");
        await app.Annotations.AddIconAsync(sheet.Id, 0, "forte", 0, 0, 0.1, 0.1);
        await app.Bookmarks.AddBookmarkAsync(sheet.Id, 0);
        await app.Database.Connection.ExecuteAsync("UPDATE Annotation SET SyncId = NULL");
        await app.Database.Connection.ExecuteAsync("UPDATE Bookmark SET SyncId = ''");

        await app.Database.InitializeAsync();

        var annotation = await app.Database.Connection.Table<Annotation>().FirstAsync();
        var bookmark = await app.Database.Connection.Table<Bookmark>().FirstAsync();
        Assert.Equal(32, annotation.SyncId.Length);
        Assert.Equal(32, bookmark.SyncId.Length);
    }

    [Theory]
    [InlineData("a/b:c*d?e\"f<g>h|i", "a_b_c_d_e_f_g_h_i")]
    [InlineData("  ..name.. ", "name")]
    [InlineData("...", "fallback")]
    [InlineData("", "fallback")]
    public void SanitizeSegment_ProducesSafeNames(string input, string expected) =>
        Assert.Equal(expected, ExternalPaths.SanitizeSegment(input, "fallback"));

    [Fact]
    public void SanitizeSegment_LimitsLength() =>
        Assert.True(ExternalPaths.SanitizeSegment(new string('x', 500), "f").Length <= 100);
}
