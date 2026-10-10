using System.Diagnostics;
using System.Text;

namespace PiccoloReader.E2E.Infrastructure;

/// <summary>Thin wrapper over the adb CLI (on PATH, or under ANDROID_HOME/platform-tools).</summary>
public static class Adb
{
    private static string Executable
    {
        get
        {
            var sdk = Environment.GetEnvironmentVariable("ANDROID_HOME")
                      ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
            if (sdk is not null)
            {
                var candidate = Path.Combine(sdk, "platform-tools", OperatingSystem.IsWindows() ? "adb.exe" : "adb");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return "adb";
        }
    }

    private static ProcessStartInfo Start(IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(Executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        if (E2EEnvironment.AdbSerial is { Length: > 0 } serial)
        {
            psi.ArgumentList.Add("-s");
            psi.ArgumentList.Add(serial);
        }

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        return psi;
    }

    /// <summary>Runs adb and returns stdout. Throws on a non-zero exit unless <paramref name="allowFailure"/>.</summary>
    public static string Run(bool allowFailure, params string[] args)
    {
        using var process = Process.Start(Start(args))!;
        var stderrTask = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(true);
            throw new TimeoutException($"adb {string.Join(' ', args)} timed out");
        }

        if (process.ExitCode != 0 && !allowFailure)
        {
            throw new InvalidOperationException(
                $"adb {string.Join(' ', args)} failed ({process.ExitCode}): {stderrTask.Result}{stdout}");
        }

        return stdout;
    }

    public static string Run(params string[] args) => Run(false, args);

    /// <summary>
    /// Runs a command inside the app's sandbox (requires a debuggable APK).
    /// The command line is handed to the device shell as a single string.
    /// </summary>
    public static string RunAs(string command, bool allowFailure = false) =>
        Run(allowFailure, "shell", $"run-as {E2EEnvironment.PackageName} {command}");

    /// <summary>Copies a file out of the app sandbox byte-for-byte (adb exec-out avoids CRLF mangling).</summary>
    public static bool PullFromApp(string relativePath, string localPath)
    {
        using var process = Process.Start(Start(new[] { "exec-out", $"run-as {E2EEnvironment.PackageName} cat {relativePath}" }))!;
        using (var file = File.Create(localPath))
        {
            process.StandardOutput.BaseStream.CopyTo(file);
        }

        process.WaitForExit(60_000);
        var ok = process.ExitCode == 0 && new FileInfo(localPath).Length > 0;
        if (!ok)
        {
            File.Delete(localPath);
        }

        return ok;
    }

    /// <summary>Writes a local file into the app sandbox via /data/local/tmp + run-as.</summary>
    public static void PushToApp(string localPath, string relativeDestination)
    {
        var staging = $"/data/local/tmp/e2e-{Guid.NewGuid():N}";
        Run("push", localPath, staging);
        try
        {
            RunAs($"sh -c 'mkdir -p $(dirname {relativeDestination}) && cat {staging} > {relativeDestination}'");
        }
        finally
        {
            Run(true, "shell", $"rm -f {staging}");
        }
    }

    public static void WriteTextToApp(string relativeDestination, string content)
    {
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
        RunAs($"sh -c 'mkdir -p $(dirname {relativeDestination}) && echo {b64} | base64 -d > {relativeDestination}'");
    }

    public static string? ReadTextFromApp(string relativePath)
    {
        var text = RunAs($"cat {relativePath}", allowFailure: true);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public static void Shell(string command) => Run("shell", command);

    public static string Logcat(int lines = 400) => Run(true, "logcat", "-d", "-t", lines.ToString());
}
