using PiccoloReader.E2E.Infrastructure;

namespace PiccoloReader.E2E.Screens;

/// <summary>Page object for the Piano tab (PianoPage), keyed on AutomationIds.</summary>
public static class PianoScreen
{
    public const string Tab = "TabPiano";
    public const string InstrumentButton = "PianoInstrumentButton";
    public const string InstrumentSheet = "PianoInstrumentSheet";
    public const string KeysLabel = "PianoKeysLabel";
    public const string KeysPlus = "PianoKeysPlus";
    public const string KeysMinus = "PianoKeysMinus";

    public static string InstrumentRow(string id) => "PianoInstrument_" + id;

    public static void Open(string title)
    {
        if (Ui.TryFind(Tab) is { } tab)
        {
            tab.Click();
        }
        else
        {
            Ui.TapText(title);
        }

        Ui.Find(InstrumentButton, 30);
    }
}
