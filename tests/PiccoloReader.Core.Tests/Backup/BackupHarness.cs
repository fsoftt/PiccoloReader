using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services.Backup;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.Backup;

// One simulated device: its own storage, database and preferences.
public sealed class BackupHarness : IDisposable
{
    public BackupHarness(FakeDriveClient? drive = null)
    {
        Batteries_V2.Init();
        Storage = new TestAppStorageProvider();
        Database = new AppDatabase(Storage.DatabasePath);
        Database.InitializeAsync().GetAwaiter().GetResult();
        Drive = drive ?? new FakeDriveClient();
        State = new FakeBackupStateStore();
        Reading = new FakeReadingPreferenceService();
        Language = new FakeLanguagePreferenceService();
        Ads = new FakeAdsPreferenceService();
        Service = new BackupService(Database, Storage, Drive, Reading, Language, Ads, State, new BackupOptions("1.1.0"));
        Directory.CreateDirectory(Storage.SheetsDirectory);
    }

    public TestAppStorageProvider Storage { get; }
    public AppDatabase Database { get; }
    public FakeDriveClient Drive { get; }
    public FakeBackupStateStore State { get; }
    public FakeReadingPreferenceService Reading { get; }
    public FakeLanguagePreferenceService Language { get; }
    public FakeAdsPreferenceService Ads { get; }
    public BackupService Service { get; }

    public string PathOf(Sheet sheet) => Path.Combine(Storage.SheetsDirectory, sheet.FileName);

    public async Task<Folder> AddFolderAsync(string name)
    {
        var folder = new Folder { Name = name, DateAdded = DateTime.UtcNow };
        await Database.Connection.InsertAsync(folder);
        return folder;
    }

    public async Task<Sheet> AddSheetAsync(string title, string content, int? folderId = null, bool writeFile = true)
    {
        var sheet = new Sheet
        {
            Title = title,
            FileName = Guid.NewGuid().ToString("N") + ".pdf",
            FolderId = folderId,
            PageCount = 3,
            LastViewedPageIndex = 2,
            DateAdded = DateTime.UtcNow
        };
        if (writeFile)
        {
            await File.WriteAllTextAsync(PathOf(sheet), content);
        }

        await Database.Connection.InsertAsync(sheet);
        return sheet;
    }

    public async Task AddAnnotationsAsync(Sheet sheet, string iconKey)
    {
        await Database.Connection.InsertAsync(new Annotation
        {
            SheetId = sheet.Id, PageIndex = 1, Type = AnnotationType.Icon, IconKey = iconKey,
            X = 0.25, Y = 0.5, Width = 0.1, Height = 0.1, ColorHex = "#FF0000",
            CreatedAt = DateTime.UtcNow, CoordinateSpace = AnnotationCoordinateSpace.Page
        });
        await Database.Connection.InsertAsync(new Annotation
        {
            SheetId = sheet.Id, PageIndex = 0, Type = AnnotationType.Stroke, StrokeWidth = 0.01,
            Points = "[{\"X\":0.1,\"Y\":0.2}]", CreatedAt = DateTime.UtcNow, CoordinateSpace = AnnotationCoordinateSpace.Legacy
        });
    }

    public async Task AddBookmarkAndCropAsync(Sheet sheet)
    {
        await Database.Connection.InsertAsync(new Bookmark { SheetId = sheet.Id, PageIndex = 1, Name = "Coda", CreatedAt = DateTime.UtcNow });
        await Database.Connection.InsertAsync(new SheetPageCrop { SheetId = sheet.Id, PageIndex = 0, Left = 0.1, Top = 0.2, Right = 0.9, Bottom = 0.8 });
    }

    public void Dispose() => Storage.Dispose();
}
