namespace PiccoloReader.Core.Services.Concert;

/// <summary>When each stage of the concert mode tutorial is due.</summary>
public static class ConcertTutorialPolicy
{
    /// <param name="handlesVisible">Drag handles are shown (Android, at least two sheets, no search).</param>
    public static bool ShouldShowLibraryStage(bool concertModeEnabled, bool librarySeen, bool pageVisible, bool handlesVisible) =>
        concertModeEnabled && !librarySeen && pageVisible && handlesVisible;

    /// <param name="hasNeighbourScore">The sheet came from a list and has a next or previous score.</param>
    public static bool ShouldShowViewerStage(bool concertModeEnabled, bool viewerSeen, bool pageVisible, bool hasNeighbourScore) =>
        concertModeEnabled && !viewerSeen && pageVisible && hasNeighbourScore;

    /// <summary>State of the Settings switch: on while any stage is still pending.</summary>
    public static bool IsTutorialPending(IConcertTutorialPreferenceService preferences) =>
        !(preferences.LibrarySeen && preferences.ViewerSeen);
}
