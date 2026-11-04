namespace PiccoloReader.Core.Services.Concert;

/// <summary>
/// Remembers which stages of the concert mode tutorial (coach marks) the user
/// has finished or skipped. Both default to false, so installs that update
/// to the version with concert mode also see the guide.
/// </summary>
public interface IConcertTutorialPreferenceService
{
    /// <summary>The library (reorder handles, sort) stage was finished or skipped.</summary>
    bool LibrarySeen { get; set; }

    /// <summary>The viewer (tap zones) stage was finished or skipped.</summary>
    bool ViewerSeen { get; set; }

    /// <summary>Marks both stages as not seen, so the guide shows again.</summary>
    void Reset();
}
