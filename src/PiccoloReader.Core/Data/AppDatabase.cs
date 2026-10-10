using PiccoloReader.Core.Data.Models;
using SQLite;

namespace PiccoloReader.Core.Data;

public class AppDatabase
{
    public AppDatabase(string databasePath)
    {
        Connection = new SQLiteAsyncConnection(databasePath);
    }

    public SQLiteAsyncConnection Connection { get; }

    public async Task InitializeAsync()
    {
        await Connection.CreateTableAsync<Folder>();
        await Connection.CreateTableAsync<Sheet>();
        await Connection.CreateTableAsync<Annotation>();
        await Connection.CreateTableAsync<Bookmark>();
        await Connection.CreateTableAsync<SheetPageCrop>();

        // Backfill stable ids for rows that predate the SyncId column.
        await Connection.ExecuteAsync("UPDATE Annotation SET SyncId = lower(hex(randomblob(16))) WHERE SyncId IS NULL OR SyncId = ''");
        await Connection.ExecuteAsync("UPDATE Bookmark SET SyncId = lower(hex(randomblob(16))) WHERE SyncId IS NULL OR SyncId = ''");
    }
}
