namespace PiccoloReader.E2E.Infrastructure;

/// <summary>Everything configurable from the environment, in one place.</summary>
public static class E2EEnvironment
{
    public const string PackageName = "com.fsoftt.piccoloreader";

    /// <summary>Path to the installable APK (Debug + EmbedAssembliesIntoApk, or Release).</summary>
    public static string ApkPath =>
        Environment.GetEnvironmentVariable("PICCOLO_APK")
        ?? throw new InvalidOperationException(
            "PICCOLO_APK is not set. Point it at a built PiccoloReader APK (see tests/PiccoloReader.E2E/README.md).");

    public static Uri AppiumUrl =>
        new(Environment.GetEnvironmentVariable("APPIUM_URL") ?? "http://127.0.0.1:4723");

    /// <summary>Where screenshots, page sources and logcat dumps for failed tests are written.</summary>
    public static string ArtifactsDir
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("E2E_ARTIFACTS")
                      ?? Path.Combine(AppContext.BaseDirectory, "e2e-artifacts");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>
    /// "pmclear" (default): wipe app data between tests with <c>pm clear</c>. Only safe for
    /// self-contained APKs - on a Fast Deployment Debug build it deletes the .NET runtime
    /// payload and the app no longer starts. PICCOLO_RESET=none: do not wipe (state leaks between tests).
    /// </summary>
    public static bool ResetWithPmClear =>
        !string.Equals(Environment.GetEnvironmentVariable("PICCOLO_RESET"), "none", StringComparison.OrdinalIgnoreCase);

    /// <summary>"picker" (default) imports through the system document picker; "seed" writes the DB row + file directly.</summary>
    public static bool ImportViaSeed =>
        string.Equals(Environment.GetEnvironmentVariable("PICCOLO_IMPORT_MODE"), "seed", StringComparison.OrdinalIgnoreCase);

    public static string? AdbSerial => Environment.GetEnvironmentVariable("ANDROID_SERIAL");
}
