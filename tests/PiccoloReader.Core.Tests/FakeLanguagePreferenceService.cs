using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests;

public class FakeLanguagePreferenceService : ILanguagePreferenceService
{
    public string? SavedCode { get; set; }

    public string? GetSavedLanguageCode() => SavedCode;

    public void SaveLanguageCode(string code) => SavedCode = code;
}
