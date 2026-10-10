using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PiccoloReader.Core.Services.Backup;

// Drive REST v3 over HttpClient, limited to what the drive.file scope allows.
public class GoogleDriveClient : IDriveClient
{
    private const string FilesUrl = "https://www.googleapis.com/drive/v3/files";
    private const string UploadUrl = "https://www.googleapis.com/upload/drive/v3/files";
    private const string FolderMime = "application/vnd.google-apps.folder";
    private const string FileFields = "id,name,size,appProperties,modifiedTime";

    private readonly HttpClient _http;
    private readonly Func<CancellationToken, Task<string>> _tokenProvider;

    public GoogleDriveClient(HttpClient http, Func<CancellationToken, Task<string>> tokenProvider)
    {
        _http = http;
        _tokenProvider = tokenProvider;
    }

    public async Task<string> EnsureFolderAsync(IReadOnlyList<string> path, CancellationToken cancellationToken = default)
    {
        var parent = "root";
        foreach (var name in path)
        {
            var query = $"name = '{Escape(name)}' and mimeType = '{FolderMime}' and '{parent}' in parents and trashed = false";
            var found = await ListAsync(query, cancellationToken);
            if (found.Count > 0)
            {
                parent = found[0].Id;
                continue;
            }

            var body = JsonSerializer.Serialize(new { name, mimeType = FolderMime, parents = new[] { parent } });
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{FilesUrl}?fields={FileFields}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            using var response = await SendAsync(request, cancellationToken);
            parent = ParseFile(await response.Content.ReadAsStringAsync(cancellationToken)).Id;
        }

        return parent;
    }

    public Task<IReadOnlyList<DriveFile>> ListByKindAsync(string kind, CancellationToken cancellationToken = default) =>
        ListAsync($"appProperties has {{ key='piccoloKind' and value='{Escape(kind)}' }} and trashed = false", cancellationToken);

    public async Task<DriveFile> UploadAsync(
        string name,
        string parentId,
        Stream content,
        string mimeType,
        IReadOnlyDictionary<string, string> properties,
        string? existingFileId,
        CancellationToken cancellationToken = default)
    {
        // Resumable upload: 1) send metadata, get the session URI; 2) PUT the bytes.
        var isUpdate = existingFileId is not null;
        var metadata = isUpdate
            ? JsonSerializer.Serialize(new { name, appProperties = properties })
            : JsonSerializer.Serialize(new { name, parents = new[] { parentId }, appProperties = properties });

        var startUrl = isUpdate
            ? $"{UploadUrl}/{Uri.EscapeDataString(existingFileId!)}?uploadType=resumable&fields={FileFields}"
            : $"{UploadUrl}?uploadType=resumable&fields={FileFields}";

        using var start = new HttpRequestMessage(isUpdate ? HttpMethod.Patch : HttpMethod.Post, startUrl)
        {
            Content = new StringContent(metadata, Encoding.UTF8, "application/json")
        };
        start.Headers.Add("X-Upload-Content-Type", mimeType);

        Uri sessionUri;
        using (var startResponse = await SendAsync(start, cancellationToken))
        {
            sessionUri = startResponse.Headers.Location
                ?? throw new DriveException("Google Drive did not return an upload location.");
        }

        using var upload = new HttpRequestMessage(HttpMethod.Put, sessionUri)
        {
            Content = new StreamContent(content)
        };
        upload.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);

        // The session URI already carries its authorization.
        using var uploadResponse = await SendAsync(upload, cancellationToken, authorize: false);
        return ParseFile(await uploadResponse.Content.ReadAsStringAsync(cancellationToken));
    }

    public async Task DownloadAsync(string fileId, Stream destination, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{FilesUrl}/{Uri.EscapeDataString(fileId)}?alt=media");
        using var response = await SendAsync(request, cancellationToken, completion: HttpCompletionOption.ResponseHeadersRead);
        await response.Content.CopyToAsync(destination, cancellationToken);
    }

    public async Task DeleteAsync(string fileId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{FilesUrl}/{Uri.EscapeDataString(fileId)}");
        try
        {
            using var response = await SendAsync(request, cancellationToken);
        }
        catch (DriveFileNotFoundException)
        {
            // Already gone (the user removed it): that is the goal.
        }
    }

    public async Task<string?> GetAccountEmailAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/drive/v3/about?fields=user(emailAddress)");
        using var response = await SendAsync(request, cancellationToken);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return doc.RootElement.TryGetProperty("user", out var user) && user.TryGetProperty("emailAddress", out var email)
            ? email.GetString()
            : null;
    }

    private async Task<IReadOnlyList<DriveFile>> ListAsync(string query, CancellationToken ct)
    {
        var files = new List<DriveFile>();
        string? pageToken = null;
        do
        {
            var url = $"{FilesUrl}?q={Uri.EscapeDataString(query)}&spaces=drive&pageSize=1000&fields=nextPageToken,files({FileFields})";
            if (pageToken is not null)
            {
                url += "&pageToken=" + Uri.EscapeDataString(pageToken);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await SendAsync(request, ct);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

            if (doc.RootElement.TryGetProperty("files", out var array))
            {
                foreach (var item in array.EnumerateArray())
                {
                    files.Add(ParseFile(item));
                }
            }

            pageToken = doc.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return files;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct,
        bool authorize = true,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        if (authorize)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _tokenProvider(ct));
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, completion, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new DriveException("Could not reach Google Drive. Check your connection.", ex);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var status = response.StatusCode;
        response.Dispose();
        throw MapError(status, body);
    }

    private static Exception MapError(HttpStatusCode status, string body)
    {
        var reason = ExtractReason(body, out var message);
        if (status == HttpStatusCode.Unauthorized)
        {
            return new DriveAuthRequiredException(message ?? "Google sign-in expired.");
        }

        if (status == HttpStatusCode.NotFound)
        {
            return new DriveFileNotFoundException(message ?? "File not found in Google Drive.");
        }

        if (reason is "storageQuotaExceeded" or "quotaExceeded")
        {
            return new DriveQuotaException(message ?? "Your Google Drive is full.");
        }

        if (status == HttpStatusCode.Forbidden && reason is "insufficientPermissions" or "authError" or "forbidden")
        {
            return new DriveAuthRequiredException(message ?? "Google Drive access was not granted.");
        }

        return new DriveException($"Google Drive error {(int)status}: {message ?? reason ?? status.ToString()}");
    }

    private static string? ExtractReason(string body, out string? message)
    {
        message = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("error", out var error))
            {
                return null;
            }

            if (error.ValueKind == JsonValueKind.Object)
            {
                message = error.TryGetProperty("message", out var m) ? m.GetString() : null;
                if (error.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                {
                    return errors[0].TryGetProperty("reason", out var r) ? r.GetString() : null;
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static DriveFile ParseFile(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseFile(doc.RootElement);
    }

    private static DriveFile ParseFile(JsonElement item)
    {
        var properties = new Dictionary<string, string>();
        if (item.TryGetProperty("appProperties", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in props.EnumerateObject())
            {
                properties[p.Name] = p.Value.GetString() ?? string.Empty;
            }
        }

        long? size = item.TryGetProperty("size", out var s) && long.TryParse(s.GetString(), out var parsed) ? parsed : null;
        DateTime? modified = item.TryGetProperty("modifiedTime", out var m)
            && DateTime.TryParse(m.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var t)
            ? t
            : null;

        return new DriveFile(
            item.GetProperty("id").GetString()!,
            item.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
            size,
            properties,
            modified);
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");
}
