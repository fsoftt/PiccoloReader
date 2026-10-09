using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Services;

public class MauiReadingPreferenceService : IReadingPreferenceService
{
    private const string ReadingDirectionKey = "ReadingDirection";

    public ReadingDirection GetReadingDirection() =>
        Enum.TryParse<ReadingDirection>(Preferences.Default.Get(ReadingDirectionKey, nameof(ReadingDirection.Horizontal)), out var direction)
            ? direction
            : ReadingDirection.Horizontal;

    public void SetReadingDirection(ReadingDirection direction) =>
        Preferences.Default.Set(ReadingDirectionKey, direction.ToString());
}
