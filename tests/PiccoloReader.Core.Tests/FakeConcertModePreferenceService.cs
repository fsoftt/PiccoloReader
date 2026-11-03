using PiccoloReader.Core.Services.Concert;

namespace PiccoloReader.Core.Tests;

public class FakeConcertModePreferenceService : IConcertModePreferenceService
{
    public bool Enabled { get; set; } = true;
}
