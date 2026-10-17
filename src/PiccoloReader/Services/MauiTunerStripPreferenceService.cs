using PiccoloReader.Core.Services.Tuner;

namespace PiccoloReader.Services;

public class MauiTunerStripPreferenceService : ITunerStripPreferenceService
{
    private const string EnabledKey = "TunerStripEnabled";
    private const string PositionKey = "TunerStripPosition";

    public bool IsEnabled
    {
        get => Preferences.Default.Get(EnabledKey, false);
        set => Preferences.Default.Set(EnabledKey, value);
    }

    public TunerStripPosition Position
    {
        get => Enum.TryParse<TunerStripPosition>(Preferences.Default.Get(PositionKey, nameof(TunerStripPosition.Right)), out var position)
            ? position
            : TunerStripPosition.Right;
        set => Preferences.Default.Set(PositionKey, value.ToString());
    }
}
