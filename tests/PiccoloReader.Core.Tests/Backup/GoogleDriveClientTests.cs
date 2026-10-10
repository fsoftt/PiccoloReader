using System.Net;
using System.Text;
using PiccoloReader.Core.Services.Backup;

namespace PiccoloReader.Core.Tests.Backup;

public class GoogleDriveClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request, body));
            return Respond(request);
        }
    }

    private static (GoogleDriveClient Client, StubHandler Handler) Create()
    {
        var handler = new StubHandler();
        return (new GoogleDriveClient(new HttpClient(handler), _ => Task.FromResult("tok")), handler);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ListByKind_ParsesFilesAndQueriesByAppProperty()
    {
        var (client, handler) = Create();
        handler.Respond = _ => Json("""
            {"files":[{"id":"1","name":"A.pdf","size":"12","appProperties":{"piccoloFile":"x.pdf","sha256":"abc"},"modifiedTime":"2026-01-02T03:04:05.000Z"}]}
            """);

        var files = await client.ListByKindAsync("sheet");

        var file = Assert.Single(files);
        Assert.Equal("1", file.Id);
        Assert.Equal(12, file.Size);
        Assert.Equal("x.pdf", file.Properties["piccoloFile"]);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), file.ModifiedUtc);
        var call = handler.Calls.Single().Request;
        Assert.Equal("Bearer tok", call.Headers.Authorization!.ToString());
        Assert.Contains("appProperties", Uri.UnescapeDataString(call.RequestUri!.Query));
        Assert.Contains("piccoloKind", Uri.UnescapeDataString(call.RequestUri.Query));
    }

    [Fact]
    public async Task List_FollowsPagination()
    {
        var (client, handler) = Create();
        var page = 0;
        handler.Respond = _ => page++ == 0
            ? Json("{\"nextPageToken\":\"n\",\"files\":[{\"id\":\"1\",\"name\":\"a\"}]}")
            : Json("{\"files\":[{\"id\":\"2\",\"name\":\"b\"}]}");

        var files = await client.ListByKindAsync("sheet");

        Assert.Equal(new[] { "1", "2" }, files.Select(f => f.Id));
        Assert.Contains("pageToken=n", handler.Calls[1].Request.RequestUri!.Query);
    }

    [Fact]
    public async Task EnsureFolder_CreatesMissingFoldersUnderRoot()
    {
        var (client, handler) = Create();
        var n = 0;
        handler.Respond = req => req.Method == HttpMethod.Get
            ? Json("{\"files\":[]}")
            : Json($"{{\"id\":\"f{++n}\",\"name\":\"x\"}}");

        var id = await client.EnsureFolderAsync(new[] { "PiccoloReader", "Partituras" });

        Assert.Equal("f2", id);
        var creates = handler.Calls.Where(c => c.Request.Method == HttpMethod.Post).ToList();
        Assert.Equal(2, creates.Count);
        Assert.Contains("\"parents\":[\"root\"]", creates[0].Body);
        Assert.Contains("\"parents\":[\"f1\"]", creates[1].Body);
        Assert.Contains("application/vnd.google-apps.folder", creates[0].Body);
    }

    [Fact]
    public async Task EnsureFolder_ReusesExistingFolder()
    {
        var (client, handler) = Create();
        handler.Respond = _ => Json("{\"files\":[{\"id\":\"existing\",\"name\":\"PiccoloReader\"}]}");

        Assert.Equal("existing", await client.EnsureFolderAsync(new[] { "PiccoloReader" }));
        Assert.DoesNotContain(handler.Calls, c => c.Request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Upload_NewFile_StartsResumableSessionThenPutsContent()
    {
        var (client, handler) = Create();
        handler.Respond = req =>
        {
            if (req.Method == HttpMethod.Post)
            {
                var start = new HttpResponseMessage(HttpStatusCode.OK);
                start.Headers.Location = new Uri("https://upload.example/session/1");
                return start;
            }

            return Json("{\"id\":\"new\",\"name\":\"A.pdf\"}");
        };

        var props = new Dictionary<string, string> { ["piccoloKind"] = "sheet" };
        var file = await client.UploadAsync("A.pdf", "parent", new MemoryStream(new byte[] { 1, 2, 3 }), "application/pdf", props, null);

        Assert.Equal("new", file.Id);
        Assert.Equal(2, handler.Calls.Count);
        Assert.Contains("uploadType=resumable", handler.Calls[0].Request.RequestUri!.Query);
        Assert.Contains("\"parents\":[\"parent\"]", handler.Calls[0].Body);
        Assert.Contains("piccoloKind", handler.Calls[0].Body);
        Assert.Equal(HttpMethod.Put, handler.Calls[1].Request.Method);
        Assert.Equal("https://upload.example/session/1", handler.Calls[1].Request.RequestUri!.ToString());
        Assert.Equal("application/pdf", handler.Calls[1].Request.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Upload_ExistingFile_PatchesWithoutParents()
    {
        var (client, handler) = Create();
        handler.Respond = req =>
        {
            if (req.Method == HttpMethod.Patch)
            {
                var start = new HttpResponseMessage(HttpStatusCode.OK);
                start.Headers.Location = new Uri("https://upload.example/session/2");
                return start;
            }

            return Json("{\"id\":\"abc\",\"name\":\"A.pdf\"}");
        };

        await client.UploadAsync("A.pdf", "parent", new MemoryStream(new byte[] { 1 }), "application/pdf", new Dictionary<string, string>(), "abc");

        Assert.Equal(HttpMethod.Patch, handler.Calls[0].Request.Method);
        Assert.Contains("/files/abc", handler.Calls[0].Request.RequestUri!.AbsolutePath);
        Assert.DoesNotContain("parents", handler.Calls[0].Body);
    }

    [Fact]
    public async Task Download_WritesBytes_AndMapsNotFound()
    {
        var (client, handler) = Create();
        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 9, 8, 7 }) };
        using var ms = new MemoryStream();
        await client.DownloadAsync("id", ms);
        Assert.Equal(new byte[] { 9, 8, 7 }, ms.ToArray());
        Assert.Contains("alt=media", handler.Calls.Single().Request.RequestUri!.Query);

        handler.Respond = _ => Json("{\"error\":{\"code\":404,\"message\":\"File not found\"}}", HttpStatusCode.NotFound);
        await Assert.ThrowsAsync<DriveFileNotFoundException>(() => client.DownloadAsync("id", new MemoryStream()));
    }

    [Fact]
    public async Task Errors_AreMappedToDedicatedExceptions()
    {
        var (client, handler) = Create();

        handler.Respond = _ => Json("{\"error\":{\"message\":\"Invalid Credentials\"}}", HttpStatusCode.Unauthorized);
        await Assert.ThrowsAsync<DriveAuthRequiredException>(() => client.ListByKindAsync("sheet"));

        handler.Respond = _ => Json("{\"error\":{\"message\":\"full\",\"errors\":[{\"reason\":\"storageQuotaExceeded\"}]}}", HttpStatusCode.Forbidden);
        await Assert.ThrowsAsync<DriveQuotaException>(() => client.ListByKindAsync("sheet"));

        handler.Respond = _ => Json("{}", HttpStatusCode.InternalServerError);
        await Assert.ThrowsAsync<DriveException>(() => client.ListByKindAsync("sheet"));
    }

    [Fact]
    public async Task Delete_IgnoresAlreadyDeletedFile()
    {
        var (client, handler) = Create();
        handler.Respond = _ => Json("{\"error\":{\"message\":\"gone\"}}", HttpStatusCode.NotFound);

        await client.DeleteAsync("id");
    }

    [Fact]
    public async Task GetAccountEmail_ReadsAboutUser()
    {
        var (client, handler) = Create();
        handler.Respond = _ => Json("{\"user\":{\"emailAddress\":\"me@example.com\"}}");

        Assert.Equal("me@example.com", await client.GetAccountEmailAsync());
    }
}
