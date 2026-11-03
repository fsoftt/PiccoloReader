using PiccoloReader.Core.Services.Concert;

namespace PiccoloReader.Services;

/// <summary>Decides and records when the concert mode coach marks show (see ConcertTutorialPolicy).</summary>
public class ConcertTutorialService
{
    private readonly IConcertModePreferenceService _concertMode;
    private readonly IConcertTutorialPreferenceService _preferences;

    public ConcertTutorialService(IConcertModePreferenceService concertMode, IConcertTutorialPreferenceService preferences)
    {
        _concertMode = concertMode;
        _preferences = preferences;
    }

    public bool ShouldShowLibrary(bool pageVisible, bool handlesVisible) =>
        ConcertTutorialPolicy.ShouldShowLibraryStage(_concertMode.Enabled, _preferences.LibrarySeen, pageVisible, handlesVisible);

    public bool ShouldShowViewer(bool pageVisible, bool hasNeighbourScore) =>
        ConcertTutorialPolicy.ShouldShowViewerStage(_concertMode.Enabled, _preferences.ViewerSeen, pageVisible, hasNeighbourScore);

    public void MarkLibrarySeen() => _preferences.LibrarySeen = true;

    public void MarkViewerSeen() => _preferences.ViewerSeen = true;
}
