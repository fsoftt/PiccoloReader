using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using SQLite;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.Data;

public class AppDatabaseTests : IDisposable
{
    private readonly string _dbPath;

    public AppDatabaseTests()
    {
        Batteries_V2.Init();
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db3");
    }

    public void Dispose()
    {
        // sqlite-net-pcl pools connections per path; the pooled connection
        // still holds the file open (locked on Windows) until the pool is
        // reset, so ResetPool() must run before the file can be deleted.
        SQLiteAsyncConnection.ResetPool();

        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public async Task InitializeAsync_CreatesFolderAndSheetTables()
    {
        var database = new AppDatabase(_dbPath);
        await database.InitializeAsync();

        var folder = new Folder { Name = "Orchestra" };
        await database.Connection.InsertAsync(folder);

        var sheet = new Sheet
        {
            FolderId = folder.Id,
            Title = "Symphony No. 5",
            FileName = "abc123.pdf",
            PageCount = 0,
            DateAdded = DateTime.UtcNow
        };
        await database.Connection.InsertAsync(sheet);

        var folders = await database.Connection.Table<Folder>().ToListAsync();
        var sheets = await database.Connection.Table<Sheet>().ToListAsync();

        Assert.Single(folders);
        Assert.Single(sheets);
        Assert.Equal(folder.Id, sheets[0].FolderId);
    }
}
