using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests;

public class FakeReadingPreferenceService : IReadingPreferenceService
{
    public ReadingMode Mode { get; set; } = ReadingMode.Horizontal;

    public ReadingMode GetReadingMode() => Mode;

    public void SetReadingMode(ReadingMode mode) => Mode = mode;
}
