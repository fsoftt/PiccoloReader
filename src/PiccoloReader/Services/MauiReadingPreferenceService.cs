using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Services;

public class MauiReadingPreferenceService : IReadingPreferenceService
{
    private const string ReadingModeKey = "ReadingMode";

    public ReadingMode GetReadingMode() =>
        Enum.TryParse<ReadingMode>(Preferences.Default.Get(ReadingModeKey, nameof(ReadingMode.Horizontal)), out var mode)
            ? mode
            : ReadingMode.Horizontal;

    public void SetReadingMode(ReadingMode mode) =>
        Preferences.Default.Set(ReadingModeKey, mode.ToString());
}
