using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Services;

public interface IReadingPreferenceService
{
    ReadingMode GetReadingMode();

    void SetReadingMode(ReadingMode mode);
}
