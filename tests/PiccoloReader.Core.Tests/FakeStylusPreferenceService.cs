using PiccoloReader.Core.Services.Stylus;

namespace PiccoloReader.Core.Tests;

public class FakeStylusPreferenceService : IStylusPreferenceService
{
    public bool DrawsDirectly { get; set; } = true;

    public bool RecognizesShapes { get; set; } = true;

    public int ShapeHintsShown { get; set; }
}
