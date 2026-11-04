using PiccoloReader.E2E.Infrastructure;
using PiccoloReader.E2E.Screens;

namespace PiccoloReader.E2E.Tests;

/// <summary>Importing a PDF through the system document picker (empty library).</summary>
[TestFixture]
public class ImportTests : E2ETestBase
{
    protected override IReadOnlyList<SeedSheet> Sheets => Array.Empty<SeedSheet>();

    [Test]
    public void ImportPdf_ViaPicker_ShowsInLibraryAndOpensInViewer()
    {
        var pdf = PdfFactory.CreatePortrait(
            Path.Combine(Path.GetTempPath(), "piccolo-e2e", "Imported Score.pdf"), 3);

        // Empty library: the header button and the empty-state button both import.
        LibraryScreen.ImportPdfViaPicker(pdf);

        LibraryScreen.SheetRow("Imported Score");
        Assert.That(AppDb.Sheets().Select(s => s.Title), Does.Contain("Imported Score"));

        LibraryScreen.OpenSheet("Imported Score");
        ViewerScreen.RevealChrome();
        Assert.That(ViewerScreen.Indicator.Total, Is.EqualTo(3));
    }
}
