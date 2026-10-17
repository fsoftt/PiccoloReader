using PiccoloReader.Core.Services.Tuner;

namespace PiccoloReader.Core.Tests;

public class FakeTunerStripPreferenceService : ITunerStripPreferenceService
{
    public bool IsEnabled { get; set; }

    public TunerStripPosition Position { get; set; } = TunerStripPosition.Right;
}
