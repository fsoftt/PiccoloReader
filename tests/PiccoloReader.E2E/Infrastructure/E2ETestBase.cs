using OpenQA.Selenium;
using PiccoloReader.E2E.Screens;

namespace PiccoloReader.E2E.Infrastructure;

/// <summary>
/// Base for every E2E fixture: gives each test a fresh app (empty library, tutorial skipped,
/// English, portrait) and attaches a screenshot / page source / logcat when a test fails.
/// </summary>
public abstract class E2ETestBase
{
    protected const string SheetTitle = "e2e-sheet";
    protected const int SheetPages = 3;

    private static string? _samplePdf;

    /// <summary>A 3-page portrait PDF, generated once per run.</summary>
    protected static string SamplePdf =>
        _samplePdf ??= PdfFactory.CreatePortrait(
            Path.Combine(Path.GetTempPath(), "piccolo-e2e", $"{SheetTitle}.pdf"), SheetPages);

    /// <summary>Reading mode written into the app's preferences before launch.</summary>
    protected virtual string InitialReadingMode => "Horizontal";

    [SetUp]
    public void ResetAndImport()
    {
        AppSession.ResetApp(InitialReadingMode);
        LibraryScreen.WaitLoaded();
        LibraryScreen.ImportPdf(SamplePdf);
    }

    /// <summary>Opens the imported sample sheet in the viewer.</summary>
    protected static void OpenSample() => LibraryScreen.OpenSheet(SheetTitle);

    [TearDown]
    public void CaptureOnFailure()
    {
        var status = TestContext.CurrentContext.Result.Outcome.Status;
        if (status != NUnit.Framework.Interfaces.TestStatus.Failed)
        {
            return;
        }

        var safeName = string.Concat(TestContext.CurrentContext.Test.FullName.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        var dir = E2EEnvironment.ArtifactsDir;

        Capture(() =>
        {
            var shot = ((ITakesScreenshot)AppSession.Driver).GetScreenshot();
            var path = Path.Combine(dir, safeName + ".png");
            File.WriteAllBytes(path, Convert.FromBase64String(shot.AsBase64EncodedString));
            TestContext.AddTestAttachment(path, "screenshot on failure");
        });
        Capture(() =>
        {
            var path = Path.Combine(dir, safeName + ".pagesource.xml");
            File.WriteAllText(path, AppSession.Driver.PageSource);
            TestContext.AddTestAttachment(path, "UI hierarchy on failure");
        });
        Capture(() =>
        {
            var path = Path.Combine(dir, safeName + ".logcat.txt");
            File.WriteAllText(path, Adb.Logcat(1500));
            TestContext.AddTestAttachment(path, "logcat on failure");
        });
    }

    private static void Capture(Action action)
    {
        try { action(); }
        catch (Exception ex) { TestContext.Progress.WriteLine($"Failure capture failed: {ex.Message}"); }
    }
}
