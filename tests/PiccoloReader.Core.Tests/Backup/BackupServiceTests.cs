using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services.Backup;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests.Backup;

public class BackupServiceTests : IDisposable
{
    private readonly List<BackupHarness> _devices = new();
    private readonly FakeDriveClient _drive = new();

    private BackupHarness NewDevice()
    {
        var device = new BackupHarness(_drive);
        _devices.Add(device);
        return device;
    }

    public void Dispose()
    {
        foreach (var device in _devices)
        {
            device.Dispose();
        }
    }

    // ------------------------------------------------------------ backup

    [Fact]
    public async Task Backup_FirstRun_CreatesLayoutAndUploadsEverything()
    {
        var a = NewDevice();
        var folder = await a.AddFolderAsync("Orchestra");
        await a.AddSheetAsync("Symphony", "pdf-1", folder.Id);
        await a.AddSheetAsync("Etude", "pdf-2");

        var result = await a.Service.BackupAsync();

        Assert.Equal(2, result.Uploaded);
        Assert.Equal(0, result.Unchanged);
        Assert.Contains("PiccoloReader", _drive.Folders.Keys);
        Assert.Contains("PiccoloReader/Partituras", _drive.Folders.Keys);
        var sheets = _drive.OfKind(BackupPlanner.SheetKind).ToList();
        Assert.Equal(new[] { "Etude.pdf", "Symphony.pdf" }, sheets.Select(s => s.Name).OrderBy(n => n));
        Assert.All(sheets, s => Assert.Equal(_drive.Folders["PiccoloReader/Partituras"], s.ParentId));
        Assert.All(sheets, s => Assert.Equal(64, s.Properties[BackupPlanner.HashProperty].Length));

        var snapshot = Assert.Single(_drive.OfKind(BackupPlanner.SnapshotKind));
        Assert.Equal("piccolo-library.json", snapshot.Name);
        Assert.Equal(_drive.Folders["PiccoloReader"], snapshot.ParentId);
        var info = Assert.Single(_drive.OfKind(BackupPlanner.InfoKind));
        Assert.Equal("backup-info.json", info.Name);
        Assert.Contains("\"SheetCount\": 2", System.Text.Encoding.UTF8.GetString(info.Content));
        Assert.Contains("\"AppVersion\": \"1.1.0\"", System.Text.Encoding.UTF8.GetString(info.Content));
    }

    [Fact]
    public async Task Backup_SecondRunWithoutChanges_UploadsNoSheets()
    {
        var a = NewDevice();
        await a.AddSheetAsync("Symphony", "pdf-1");
        await a.Service.BackupAsync();
        var sheetUploads = _drive.UploadCalls;

        var result = await a.Service.BackupAsync();

        Assert.Equal(0, result.Uploaded);
        Assert.Equal(1, result.Unchanged);
        // Only the snapshot and info are refreshed.
        Assert.Equal(sheetUploads + 2, _drive.UploadCalls);
        Assert.Single(_drive.OfKind(BackupPlanner.SnapshotKind));
        Assert.Single(_drive.OfKind(BackupPlanner.InfoKind));
    }

    [Fact]
    public async Task Backup_ChangedAndNewSheets_UploadsOnlyThose()
    {
        var a = NewDevice();
        var changed = await a.AddSheetAsync("Changed", "v1");
        await a.AddSheetAsync("Same", "same");
        await a.Service.BackupAsync();
        var remoteId = _drive.OfKind(BackupPlanner.SheetKind).Single(f => f.Name == "Changed.pdf").Id;

        await File.WriteAllTextAsync(a.PathOf(changed), "v2 longer content");
        await a.AddSheetAsync("Brand New", "new");
        var result = await a.Service.BackupAsync();

        Assert.Equal(2, result.Uploaded);
        Assert.Equal(1, result.Unchanged);
        Assert.Equal(3, _drive.OfKind(BackupPlanner.SheetKind).Count());
        // The changed sheet updates its existing Drive file instead of duplicating it.
        Assert.Equal("v2 longer content", System.Text.Encoding.UTF8.GetString(_drive.Files[remoteId].Content));
    }

    [Fact]
    public async Task Backup_FileDeletedInDrive_IsUploadedAgain()
    {
        var a = NewDevice();
        await a.AddSheetAsync("Symphony", "pdf-1");
        await a.Service.BackupAsync();
        var remote = _drive.OfKind(BackupPlanner.SheetKind).Single();
        _drive.Files.Remove(remote.Id);

        var result = await a.Service.BackupAsync();

        Assert.Equal(1, result.Uploaded);
        Assert.Single(_drive.OfKind(BackupPlanner.SheetKind));
    }

    [Fact]
    public async Task Backup_FileRenamedOrMovedInDrive_IsStillRecognized()
    {
        var a = NewDevice();
        await a.AddSheetAsync("Symphony", "pdf-1");
        await a.Service.BackupAsync();
        var remote = _drive.OfKind(BackupPlanner.SheetKind).Single();
        remote.Name = "my renamed copy.pdf";
        remote.ParentId = "somewhere-else";

        var result = await a.Service.BackupAsync();

        Assert.Equal(0, result.Uploaded);
        Assert.Equal(1, result.Unchanged);
    }

    [Fact]
    public async Task Backup_SheetRemovedLocally_RemovesItsDriveCopy()
    {
        var a = NewDevice();
        var gone = await a.AddSheetAsync("Gone", "x");
        await a.AddSheetAsync("Kept", "y");
        await a.Service.BackupAsync();

        await a.Database.Connection.DeleteAsync(gone);
        File.Delete(a.PathOf(gone));
        var result = await a.Service.BackupAsync();

        Assert.Equal(1, result.RemovedFromDrive);
        Assert.Equal(new[] { "Kept.pdf" }, _drive.OfKind(BackupPlanner.SheetKind).Select(f => f.Name));
    }

    [Fact]
    public async Task Backup_SameTitles_GetUniqueDriveNames()
    {
        var a = NewDevice();
        await a.AddSheetAsync("Etude", "1");
        await a.AddSheetAsync("Etude", "2");
        await a.AddSheetAsync("a/b:c", "3");

        await a.Service.BackupAsync();

        var names = _drive.OfKind(BackupPlanner.SheetKind).Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "Etude (2).pdf", "Etude.pdf", "a_b_c.pdf" }, names);
    }

    [Fact]
    public async Task Backup_SheetWithMissingLocalFile_IsReportedAndExcludedFromSnapshot()
    {
        var a = NewDevice();
        await a.AddSheetAsync("Present", "ok");
        var lost = await a.AddSheetAsync("Lost", "x", writeFile: false);
        await a.AddAnnotationsAsync(lost, "note");

        var result = await a.Service.BackupAsync();

        Assert.Equal(new[] { "Lost" }, result.MissingLocalFiles);
        Assert.Equal(1, result.Uploaded);
        var snapshot = BackupService.Deserialize(
            System.Text.Encoding.UTF8.GetString(_drive.OfKind(BackupPlanner.SnapshotKind).Single().Content));
        Assert.Equal(new[] { "Present" }, snapshot.Sheets.Select(s => s.Title));
        Assert.Empty(snapshot.Annotations);
    }

    [Fact]
    public async Task Backup_Success_RecordsStateAndReportsProgress()
    {
        var a = NewDevice();
        await a.AddSheetAsync("S", "x");
        a.State.NeedsSignIn = true;
        a.State.LastBackupError = "old";
        var stages = new List<BackupStage>();

        await a.Service.BackupAsync(new SyncProgress(p => stages.Add(p.Stage)));

        Assert.NotNull(a.State.LastBackupUtc);
        Assert.Null(a.State.LastBackupError);
        Assert.False(a.State.NeedsSignIn);
        Assert.Contains(BackupStage.UploadingSheets, stages);
        Assert.Contains(BackupStage.UploadingLibrary, stages);
        Assert.False(string.IsNullOrEmpty(a.State.HashCacheJson));
    }

    [Fact]
    public async Task Backup_AuthFailure_FlagsNeedsSignInAndKeepsLastBackup()
    {
        var a = NewDevice();
        var earlier = DateTime.UtcNow.AddDays(-1);
        a.State.LastBackupUtc = earlier;
        _drive.FailListWith = new DriveAuthRequiredException("expired");

        await Assert.ThrowsAsync<DriveAuthRequiredException>(() => a.Service.BackupAsync());

        Assert.True(a.State.NeedsSignIn);
        Assert.Equal("expired", a.State.LastBackupError);
        Assert.Equal(earlier, a.State.LastBackupUtc);
    }

    // ---------------------------------------------------------- snapshot

    [Fact]
    public async Task Snapshot_SerializeDeserialize_RoundTripsEverything()
    {
        var a = NewDevice();
        var folder = await a.AddFolderAsync("Orchestra");
        var sheet = await a.AddSheetAsync("Symphony", "x", folder.Id);
        await a.AddAnnotationsAsync(sheet, "quarter-note");
        await a.AddBookmarkAndCropAsync(sheet);
        a.Reading.Mode = ReadingMode.VerticalContinuous;
        a.Language.SavedCode = "es";
        a.Ads.Enabled = true;

        var restored = BackupService.Deserialize(BackupService.Serialize(await a.Service.BuildSnapshotAsync()));

        Assert.Equal("Orchestra", Assert.Single(restored.Folders).Name);
        Assert.Equal("Symphony", Assert.Single(restored.Sheets).Title);
        Assert.Equal(folder.Id, restored.Sheets[0].FolderId);
        Assert.Equal(2, restored.Annotations.Count);
        var icon = restored.Annotations.Single(x => x.Type == AnnotationType.Icon);
        Assert.Equal("quarter-note", icon.IconKey);
        Assert.Equal(AnnotationCoordinateSpace.Page, icon.CoordinateSpace);
        Assert.Equal("#FF0000", icon.ColorHex);
        Assert.Equal(AnnotationCoordinateSpace.Legacy, restored.Annotations.Single(x => x.Type == AnnotationType.Stroke).CoordinateSpace);
        Assert.Equal("Coda", Assert.Single(restored.Bookmarks).Name);
        Assert.Equal(0.8, Assert.Single(restored.PageCrops).Bottom);
        Assert.Equal("VerticalContinuous", restored.Preferences.ReadingMode);
        Assert.Equal("es", restored.Preferences.LanguageCode);
        Assert.True(restored.Preferences.SupportWithAds);
    }

    [Fact]
    public void Deserialize_NewerVersion_Throws()
    {
        Assert.Throws<BackupFormatException>(() => BackupService.Deserialize("{\"Version\": 99}"));
    }

    [Fact]
    public void Deserialize_Garbage_Throws()
    {
        Assert.Throws<BackupFormatException>(() => BackupService.Deserialize("not json"));
    }

    // ----------------------------------------------------------- restore

    [Fact]
    public async Task Restore_OnFreshDevice_RebuildsLibraryFilesAndPreferences()
    {
        var a = NewDevice();
        var folder = await a.AddFolderAsync("Orchestra");
        var s1 = await a.AddSheetAsync("Symphony", "content-1", folder.Id);
        var s2 = await a.AddSheetAsync("Etude", "content-2");
        await a.AddAnnotationsAsync(s1, "rest");
        await a.AddBookmarkAndCropAsync(s2);
        a.Reading.Mode = ReadingMode.VerticalPaged;
        a.Language.SavedCode = "es";
        a.Ads.Enabled = true;
        await a.Service.BackupAsync();

        var b = NewDevice();
        b.Language.SavedCode = "en";
        var restoredEvent = 0;
        b.Service.LibraryRestored += (_, _) => restoredEvent++;

        var result = await b.Service.RestoreAsync();

        Assert.Equal(2, result.RestoredSheets);
        Assert.Empty(result.MissingSheets);
        Assert.True(result.LanguageChanged);
        Assert.Equal(1, restoredEvent);

        var folders = await b.Database.Connection.Table<Folder>().ToListAsync();
        var sheets = await b.Database.Connection.Table<Sheet>().ToListAsync();
        var newFolder = Assert.Single(folders);
        var symphony = sheets.Single(s => s.Title == "Symphony");
        var etude = sheets.Single(s => s.Title == "Etude");
        Assert.Equal(newFolder.Id, symphony.FolderId);
        Assert.Null(etude.FolderId);
        Assert.Equal(s1.FileName, symphony.FileName);
        Assert.Equal(3, symphony.PageCount);
        Assert.Equal(2, symphony.LastViewedPageIndex);
        Assert.Equal("content-1", await File.ReadAllTextAsync(b.PathOf(symphony)));
        Assert.Equal("content-2", await File.ReadAllTextAsync(b.PathOf(etude)));

        var annotations = await b.Database.Connection.Table<Annotation>().ToListAsync();
        Assert.Equal(2, annotations.Count);
        Assert.All(annotations, x => Assert.Equal(symphony.Id, x.SheetId));
        Assert.Contains(annotations, x => x.IconKey == "rest" && x.CoordinateSpace == AnnotationCoordinateSpace.Page && x.X == 0.25);
        var bookmark = Assert.Single(await b.Database.Connection.Table<Bookmark>().ToListAsync());
        Assert.Equal(etude.Id, bookmark.SheetId);
        var crop = Assert.Single(await b.Database.Connection.Table<SheetPageCrop>().ToListAsync());
        Assert.Equal(etude.Id, crop.SheetId);
        Assert.Equal(0.1, crop.Left);

        Assert.Equal(ReadingMode.VerticalPaged, b.Reading.Mode);
        Assert.Equal("es", b.Language.SavedCode);
        Assert.True(b.Ads.Enabled);
    }

    [Fact]
    public async Task Restore_ReplacesCurrentLibraryAndKeepsSafetyCopy()
    {
        var a = NewDevice();
        await a.AddSheetAsync("Backed up", "from-backup");
        await a.Service.BackupAsync();

        var b = NewDevice();
        var folder = await b.AddFolderAsync("Local only");
        var local = await b.AddSheetAsync("Local piece", "local-content", folder.Id);
        await b.AddAnnotationsAsync(local, "x");

        var result = await b.Service.RestoreAsync();

        var sheets = await b.Database.Connection.Table<Sheet>().ToListAsync();
        Assert.Equal(new[] { "Backed up" }, sheets.Select(s => s.Title));
        Assert.Empty(await b.Database.Connection.Table<Folder>().ToListAsync());
        Assert.Empty(await b.Database.Connection.Table<Annotation>().ToListAsync());
        Assert.False(File.Exists(b.PathOf(local)));
        Assert.Single(Directory.GetFiles(b.Storage.SheetsDirectory));

        Assert.True(Directory.Exists(result.SafetyCopyPath));
        Assert.Equal("local-content", await File.ReadAllTextAsync(Path.Combine(result.SafetyCopyPath, "Sheets", local.FileName)));
        var safety = BackupService.Deserialize(await File.ReadAllTextAsync(Path.Combine(result.SafetyCopyPath, BackupService.SnapshotFileName)));
        Assert.Equal("Local piece", Assert.Single(safety.Sheets).Title);
        Assert.Equal("Local only", Assert.Single(safety.Folders).Name);
        Assert.Equal(2, safety.Annotations.Count);
    }

    [Fact]
    public async Task Restore_Twice_KeepsOnlyLatestSafetyCopy()
    {
        var a = NewDevice();
        await a.AddSheetAsync("S", "x");
        await a.Service.BackupAsync();
        var b = NewDevice();

        var first = await b.Service.RestoreAsync();
        var second = await b.Service.RestoreAsync();

        Assert.False(Directory.Exists(first.SafetyCopyPath));
        Assert.True(Directory.Exists(second.SafetyCopyPath));
    }

    [Fact]
    public async Task Restore_SheetMissingInDrive_SkipsItAndItsDataAndReportsIt()
    {
        var a = NewDevice();
        var kept = await a.AddSheetAsync("Kept", "kept-content");
        var lost = await a.AddSheetAsync("Lost", "lost-content");
        await a.AddAnnotationsAsync(lost, "x");
        await a.AddBookmarkAndCropAsync(lost);
        await a.AddBookmarkAndCropAsync(kept);
        await a.Service.BackupAsync();
        var userDeleted = _drive.OfKind(BackupPlanner.SheetKind).Single(f => f.Name == "Lost.pdf");
        _drive.Files.Remove(userDeleted.Id);

        var b = NewDevice();
        var result = await b.Service.RestoreAsync();

        Assert.Equal(1, result.RestoredSheets);
        Assert.Equal(new[] { "Lost" }, result.MissingSheets);
        Assert.Equal("Kept", Assert.Single(await b.Database.Connection.Table<Sheet>().ToListAsync()).Title);
        Assert.Empty(await b.Database.Connection.Table<Annotation>().ToListAsync());
        Assert.Single(await b.Database.Connection.Table<Bookmark>().ToListAsync());
        Assert.Single(await b.Database.Connection.Table<SheetPageCrop>().ToListAsync());
    }

    [Fact]
    public async Task Restore_FileMovedAndRenamedInDrive_StillRestores()
    {
        var a = NewDevice();
        await a.AddSheetAsync("Symphony", "content");
        await a.Service.BackupAsync();
        var remote = _drive.OfKind(BackupPlanner.SheetKind).Single();
        remote.Name = "renamed.pdf";
        remote.ParentId = "elsewhere";

        var b = NewDevice();
        var result = await b.Service.RestoreAsync();

        Assert.Equal(1, result.RestoredSheets);
        Assert.Empty(result.MissingSheets);
    }

    [Fact]
    public async Task Restore_WithoutBackup_ThrowsAndLeavesLibraryUntouched()
    {
        var b = NewDevice();
        var local = await b.AddSheetAsync("Local", "keep me");

        await Assert.ThrowsAsync<BackupNotFoundException>(() => b.Service.RestoreAsync());

        Assert.Equal("Local", Assert.Single(await b.Database.Connection.Table<Sheet>().ToListAsync()).Title);
        Assert.True(File.Exists(b.PathOf(local)));
    }

    [Fact]
    public async Task Restore_DownloadFailure_LeavesLibraryUntouched()
    {
        var a = NewDevice();
        await a.AddSheetAsync("Remote", "r");
        await a.Service.BackupAsync();

        var b = NewDevice();
        var local = await b.AddSheetAsync("Local", "keep me");
        _drive.FailDownloadsWith = new DriveException("network down");

        await Assert.ThrowsAsync<DriveException>(() => b.Service.RestoreAsync());

        Assert.Equal("Local", Assert.Single(await b.Database.Connection.Table<Sheet>().ToListAsync()).Title);
        Assert.Equal("keep me", await File.ReadAllTextAsync(b.PathOf(local)));
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(b.Storage.DatabasePath)!, "RestoreStaging")));
    }

    [Fact]
    public async Task Restore_ThenBackupAgain_DoesNotReuploadSheets()
    {
        var a = NewDevice();
        await a.AddSheetAsync("Symphony", "content");
        await a.Service.BackupAsync();

        var b = NewDevice();
        await b.Service.RestoreAsync();
        var result = await b.Service.BackupAsync();

        Assert.Equal(0, result.Uploaded);
        Assert.Equal(1, result.Unchanged);
    }

    // ------------------------------------------------------ auto backup

    [Fact]
    public async Task AutoBackup_Disabled_IsSkipped()
    {
        var a = NewDevice();
        a.State.AutoBackupEnabled = false;

        Assert.Equal(AutoBackupOutcome.Skipped, await a.Service.RunAutomaticBackupAsync());
        Assert.Empty(_drive.Files);
    }

    [Fact]
    public async Task AutoBackup_NotConnected_IsSkipped()
    {
        var a = NewDevice();
        a.State.AutoBackupEnabled = true;
        a.State.IsConnected = false;

        Assert.Equal(AutoBackupOutcome.Skipped, await a.Service.RunAutomaticBackupAsync());
    }

    [Fact]
    public async Task AutoBackup_Enabled_RunsBackup()
    {
        var a = NewDevice();
        a.State.AutoBackupEnabled = true;
        await a.AddSheetAsync("S", "x");

        Assert.Equal(AutoBackupOutcome.Completed, await a.Service.RunAutomaticBackupAsync());
        Assert.NotNull(a.State.LastBackupUtc);
        Assert.Single(_drive.OfKind(BackupPlanner.SheetKind));
    }

    [Fact]
    public async Task AutoBackup_NeedsInteraction_FlagsSignInInsteadOfThrowing()
    {
        var a = NewDevice();
        a.State.AutoBackupEnabled = true;
        _drive.FailListWith = new DriveAuthRequiredException("needs ui");

        Assert.Equal(AutoBackupOutcome.NeedsSignIn, await a.Service.RunAutomaticBackupAsync());
        Assert.True(a.State.NeedsSignIn);
    }

    [Fact]
    public async Task AutoBackup_NetworkError_ReturnsFailed()
    {
        var a = NewDevice();
        a.State.AutoBackupEnabled = true;
        _drive.FailListWith = new DriveException("offline");

        Assert.Equal(AutoBackupOutcome.Failed, await a.Service.RunAutomaticBackupAsync());
        Assert.False(a.State.NeedsSignIn);
        Assert.Equal("offline", a.State.LastBackupError);
    }

    private sealed class SyncProgress : IProgress<BackupProgress>
    {
        private readonly Action<BackupProgress> _action;

        public SyncProgress(Action<BackupProgress> action) => _action = action;

        public void Report(BackupProgress value) => _action(value);
    }
}
