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
    }
}
