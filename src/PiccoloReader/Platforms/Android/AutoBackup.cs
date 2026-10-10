using Android.App;
using Android.Content;
using AndroidX.Work;
using Java.Util.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using PiccoloReader.Core.Services.Backup;

namespace PiccoloReader.Platforms.Android;

// Daily backup job: runs on an unmetered network (Wi-Fi), no charging requirement.
public class BackupWorker : Worker
{
    public BackupWorker(Context context, WorkerParameters workerParams) : base(context, workerParams)
    {
    }

    public override Result DoWork()
    {
        try
        {
            var service = IPlatformApplication.Current?.Services.GetService<BackupService>();
            if (service is null)
            {
                return Result.InvokeRetry()!;
            }

            // The service resolves its own silent token; when Google needs UI it
            // flags "requires sign-in" in the settings and we simply wait.
            var outcome = Task.Run(() => service.RunAutomaticBackupAsync()).GetAwaiter().GetResult();
            return outcome == AutoBackupOutcome.Failed ? Result.InvokeRetry()! : Result.InvokeSuccess()!;
        }
        catch (Exception)
        {
            return Result.InvokeRetry()!;
        }
    }
}

public class AndroidAutoBackupScheduler : IAutoBackupScheduler
{
    private const string UniqueName = "piccolo-auto-backup";

    public void Schedule()
    {
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Unmetered!)
            .Build();

        var request = new PeriodicWorkRequest.Builder(Java.Lang.Class.FromType(typeof(BackupWorker)), 24, TimeUnit.Hours!)
            .SetConstraints(constraints)
            .Build();

        // Keep: re-scheduling on every app start must not reset the 24h timer.
        WorkManager.GetInstance(global::Android.App.Application.Context).EnqueueUniquePeriodicWork(UniqueName, ExistingPeriodicWorkPolicy.Keep!, request);
    }

    public void Cancel() =>
        WorkManager.GetInstance(global::Android.App.Application.Context).CancelUniqueWork(UniqueName);
}
