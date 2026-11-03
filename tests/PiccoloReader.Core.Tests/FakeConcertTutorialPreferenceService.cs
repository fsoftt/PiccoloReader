using PiccoloReader.Core.Services.Concert;

namespace PiccoloReader.Core.Tests;

public class FakeConcertTutorialPreferenceService : IConcertTutorialPreferenceService
{
    public bool LibrarySeen { get; set; }

    public bool ViewerSeen { get; set; }

    public int ResetCount { get; private set; }

    public void Reset()
    {
        ResetCount++;
        LibrarySeen = false;
        ViewerSeen = false;
    }
}
