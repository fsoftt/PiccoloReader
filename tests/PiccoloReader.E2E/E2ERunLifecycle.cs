using PiccoloReader.E2E.Infrastructure;

namespace PiccoloReader.E2E;

/// <summary>Disposes the shared Appium session once, after the whole run (root namespace = applies to every fixture).</summary>
[SetUpFixture]
public class E2ERunLifecycle
{
    [OneTimeTearDown]
    public void Shutdown() => AppSession.Shutdown();
}
