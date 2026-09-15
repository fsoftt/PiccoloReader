using PiccoloReader.Core.Services;
using SQLite;

namespace PiccoloReader.Core.Tests;

public class TestAppStorageProvider : IAppStorageProvider, IDisposable
{
    private readonly string _rootDirectory;

    public TestAppStorageProvider()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), $"piccolo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootDirectory);
    }

    public string DatabasePath => Path.Combine(_rootDirectory, "test.db3");

    public string SheetsDirectory => Path.Combine(_rootDirectory, "Sheets");

    public void Dispose()
    {
        // sqlite-net-pcl pools connections per path; the pooled connection
        // still holds the db file open (locked on Windows) until the pool
        // is reset, so ResetPool() must run before the directory can be
        // deleted.
        SQLiteAsyncConnection.ResetPool();

        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }
}
