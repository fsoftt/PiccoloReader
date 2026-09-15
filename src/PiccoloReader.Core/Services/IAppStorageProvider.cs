namespace PiccoloReader.Core.Services;

public interface IAppStorageProvider
{
    string DatabasePath { get; }

    string SheetsDirectory { get; }
}
