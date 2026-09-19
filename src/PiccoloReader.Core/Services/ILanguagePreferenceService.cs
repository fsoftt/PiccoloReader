namespace PiccoloReader.Core.Services;

public interface ILanguagePreferenceService
{
    string? GetSavedLanguageCode();

    void SaveLanguageCode(string code);
}
