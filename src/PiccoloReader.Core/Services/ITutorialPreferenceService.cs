namespace PiccoloReader.Core.Services;

public interface ITutorialPreferenceService
{
    bool GetTutorialCompleted();

    void SetTutorialCompleted(bool completed);
}
