using PiccoloReader.Core.Services.Stylus;

namespace PiccoloReader.Core.Tests;

public class FakeStylusPreferenceService : IStylusPreferenceService
{
    public bool DrawsDirectly { get; set; } = true;
}
