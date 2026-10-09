using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Services;

public interface IReadingPreferenceService
{
    ReadingDirection GetReadingDirection();

    void SetReadingDirection(ReadingDirection direction);
}
