using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.E2E.Infrastructure;
using PiccoloReader.E2E.Screens;

namespace PiccoloReader.E2E.Tests;

/// <summary>Piano tab: opens, key-count stepper, instrument sheet.</summary>
[TestFixture]
public class PianoTests : E2ETestBase
{
    protected override IReadOnlyList<SeedSheet> Sheets => Array.Empty<SeedSheet>();

    [Test]
    public void PianoTab_Opens_AndShowsTheKeyboardControls()
    {
        PianoScreen.Open(AppStrings.Piano);

        Assert.That(Ui.Exists(PianoScreen.KeysLabel), Is.True);
        Assert.That(Ui.TextOf(PianoScreen.InstrumentButton), Does.Contain(AppStrings.PianoGrand));
    }

    [Test]
    public void KeysPlus_ChangesTheKeyCountLabel()
    {
        PianoScreen.Open(AppStrings.Piano);
        var before = Ui.TextOf(PianoScreen.KeysLabel);

        Ui.Tap(PianoScreen.KeysPlus);

        Ui.Poll(() => Ui.TextOf(PianoScreen.KeysLabel) != before, 10, "the key count label to change");
    }

    [Test]
    public void InstrumentSheet_SelectingFlute_UpdatesTheInstrumentButton()
    {
        PianoScreen.Open(AppStrings.Piano);

        Ui.Tap(PianoScreen.InstrumentButton);
        Ui.Find(PianoScreen.InstrumentSheet, 15);
        Ui.Tap(PianoScreen.InstrumentRow("flute"));

        Ui.Poll(() => Ui.TextOf(PianoScreen.InstrumentButton).Contains(AppStrings.PianoFlute), 10, "the instrument button to show Flute");
    }
}
