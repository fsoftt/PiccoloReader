using PiccoloReader.Core.Services;

namespace PiccoloReader.Services;

public class MauiTutorialPreferenceService : ITutorialPreferenceService
{
    private const string PreferenceKey = "TutorialCompleted";

    public bool GetTutorialCompleted() =>
        Preferences.Default.Get(PreferenceKey, false);

    public void SetTutorialCompleted(bool completed) =>
        Preferences.Default.Set(PreferenceKey, completed);
}
