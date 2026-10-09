using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests;

public class FakeReadingPreferenceService : IReadingPreferenceService
{
    public ReadingDirection Direction { get; set; } = ReadingDirection.Horizontal;

    public ReadingDirection GetReadingDirection() => Direction;

    public void SetReadingDirection(ReadingDirection direction) => Direction = direction;
}
