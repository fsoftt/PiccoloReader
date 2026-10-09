using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests;

public class FakeTutorialPreferenceService : ITutorialPreferenceService
{
    public bool Completed { get; set; }

    public bool GetTutorialCompleted() => Completed;

    public void SetTutorialCompleted(bool completed) => Completed = completed;
}
