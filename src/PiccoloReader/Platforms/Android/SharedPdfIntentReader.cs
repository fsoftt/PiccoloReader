using Android.Content;
using Android.Provider;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Platforms.Android;

// Extracts the PDFs of an open-with / share intent and copies each one to the
// cache directory right away: the read grant carried by the intent is only
// temporary, so the stream must be consumed while it is valid.
internal static class SharedPdfIntentReader
{
    public static bool IsSharedPdfIntent(Intent? intent) =>
        intent?.Action is Intent.ActionView or Intent.ActionSend or Intent.ActionSendMultiple;

    public static IReadOnlyList<global::Android.Net.Uri> GetUris(Intent intent)
    {
        var uris = new List<global::Android.Net.Uri>();

        void Add(global::Android.Net.Uri? uri)
        {
            if (uri is not null && !uris.Any(u => u.ToString() == uri.ToString()))
            {
                uris.Add(uri);
            }
        }

        Add(intent.Data);

        if (intent.ClipData is { } clip)
        {
            for (var i = 0; i < clip.ItemCount; i++)
            {
                Add(clip.GetItemAt(i)?.Uri);
            }
        }

#pragma warning disable CA1422
        if (intent.Action == Intent.ActionSend)
        {
            Add(intent.GetParcelableExtra(Intent.ExtraStream) as global::Android.Net.Uri);
        }
        else if (intent.Action == Intent.ActionSendMultiple)
        {
            var list = intent.GetParcelableArrayListExtra(Intent.ExtraStream);
            if (list is not null)
            {
                foreach (var item in list)
                {
                    Add(item as global::Android.Net.Uri);
                }
            }
        }
#pragma warning restore CA1422

        return uris;
    }

    public static async Task<(List<string> Paths, int Unreadable)> CopyToCacheAsync(Context context, IReadOnlyList<global::Android.Net.Uri> uris)
    {
        var paths = new List<string>();
        var unreadable = 0;
        var root = System.IO.Path.Combine(context.CacheDir!.AbsolutePath, "shared-pdfs");

        foreach (var uri in uris)
        {
            try
            {
                var dir = System.IO.Path.Combine(root, Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(dir);
                var name = SharedPdfImportService.SanitizeFileName(GetDisplayName(context, uri), "PDF");
                var path = System.IO.Path.Combine(dir, name);

                using var input = context.ContentResolver!.OpenInputStream(uri);
                if (input is null)
                {
                    unreadable++;
                    continue;
                }

                using (var output = System.IO.File.Create(path))
                {
                    await input.CopyToAsync(output);
                }

                paths.Add(path);
            }
            catch (Exception)
            {
                unreadable++;
            }
        }

        return (paths, unreadable);
    }

    private static string? GetDisplayName(Context context, global::Android.Net.Uri uri)
    {
        try
        {
            if (uri.Scheme == ContentResolver.SchemeContent)
            {
                using var cursor = context.ContentResolver!.Query(uri, new[] { IOpenableColumns.DisplayName }, null, null, null);
                if (cursor is not null && cursor.MoveToFirst())
                {
                    var index = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
                    if (index >= 0)
                    {
                        return cursor.GetString(index);
                    }
                }
            }
        }
        catch (Exception)
        {
            // Fall back to the last path segment.
        }

        return uri.LastPathSegment;
    }
}
