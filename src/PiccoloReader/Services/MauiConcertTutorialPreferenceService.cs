using PiccoloReader.Core.Services.Concert;

namespace PiccoloReader.Services;

public class MauiConcertTutorialPreferenceService : IConcertTutorialPreferenceService
{
    private const string LibraryKey = "ConcertTutorialLibrarySeen";
    private const string ViewerKey = "ConcertTutorialViewerSeen";

    private readonly object _gate = new();

    // Cached; the constructor loads them off the UI thread. An absent key
    // means "not seen", so installs updating from before concert mode see the guide.
    private bool? _librarySeen;
    private bool? _viewerSeen;

    public MauiConcertTutorialPreferenceService()
    {
        _ = Task.Run(() =>
        {
            _ = LibrarySeen;
            _ = ViewerSeen;
        });
    }

    public bool LibrarySeen
    {
        get
        {
            lock (_gate)
            {
                return _librarySeen ??= Preferences.Default.Get(LibraryKey, false);
            }
        }
        set
        {
            lock (_gate)
            {
                _librarySeen = value;
                Preferences.Default.Set(LibraryKey, value);
            }
        }
    }

    public bool ViewerSeen
    {
        get
        {
            lock (_gate)
            {
                return _viewerSeen ??= Preferences.Default.Get(ViewerKey, false);
            }
        }
        set
        {
            lock (_gate)
            {
                _viewerSeen = value;
                Preferences.Default.Set(ViewerKey, value);
            }
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _librarySeen = false;
            _viewerSeen = false;
            Preferences.Default.Set(LibraryKey, false);
            Preferences.Default.Set(ViewerKey, false);
        }
    }
}
