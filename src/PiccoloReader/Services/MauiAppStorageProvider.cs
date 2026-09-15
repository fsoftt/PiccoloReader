using PiccoloReader.Core.Services;

namespace PiccoloReader.Services;

public class MauiAppStorageProvider : IAppStorageProvider
{
    public string DatabasePath => Path.Combine(FileSystem.AppDataDirectory, "piccoloreader.db3");

    public string SheetsDirectory => Path.Combine(FileSystem.AppDataDirectory, "Sheets");
}
