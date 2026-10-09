using Microsoft.Data.Sqlite;

namespace PiccoloReader.E2E.Infrastructure;

public record AnnotationRow(
    int Id, int SheetId, int PageIndex, string? Type, string? IconKey,
    double X, double Y, double Width, double Height,
    string? ColorHex, double StrokeWidth, string? Points, int CoordinateSpace)
{
    public bool IsStroke => Type == "Stroke";
    public bool IsIcon => !IsStroke;

    public IReadOnlyList<(double X, double Y)> StrokePoints()
    {
        if (string.IsNullOrEmpty(Points))
        {
            return Array.Empty<(double, double)>();
        }

        using var doc = System.Text.Json.JsonDocument.Parse(Points);
        return doc.RootElement.EnumerateArray()
            .Select(p => (p.GetProperty("X").GetDouble(), p.GetProperty("Y").GetDouble()))
            .ToList();
    }
}

public record BookmarkRow(int Id, int SheetId, int PageIndex, string? Name);

public record SheetRow(int Id, int? FolderId, string Title, string FileName, int PageCount, int LastViewedPageIndex);

/// <summary>
/// Read access to the app's SQLite database. The file lives in the app sandbox, so each call
/// pulls a fresh copy with <c>adb exec-out run-as ... cat</c> into a temp file and queries that.
/// </summary>
public static class AppDb
{
    public const string RelativePath = "files/piccoloreader.db3";

    static AppDb() => SQLitePCL.Batteries_V2.Init();

    public static string PullToTemp()
    {
        var local = Path.Combine(Path.GetTempPath(), $"piccolo-e2e-{Guid.NewGuid():N}.db3");
        if (!Adb.PullFromApp(RelativePath, local))
        {
            throw new InvalidOperationException("Could not read the app database (is the APK debuggable?).");
        }

        // The app does not enable WAL, but bring the journal along in case that changes.
        if (Adb.PullFromApp(RelativePath + "-wal", local + "-wal"))
        {
            Adb.PullFromApp(RelativePath + "-shm", local + "-shm");
        }

        return local;
    }

    public static void DeleteTemp(string local)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(local + suffix); } catch { /* best effort */ }
        }
    }

    public static T Query<T>(Func<SqliteConnection, T> action)
    {
        var local = PullToTemp();
        try
        {
            using var connection = new SqliteConnection($"Data Source={local};Pooling=False");
            connection.Open();
            return action(connection);
        }
        finally
        {
            DeleteTemp(local);
        }
    }

    public static List<AnnotationRow> Annotations() => Query(c =>
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id, SheetId, PageIndex, Type, IconKey, X, Y, Width, Height, ColorHex, StrokeWidth, Points, CoordinateSpace FROM Annotation ORDER BY Id";
        using var r = cmd.ExecuteReader();
        var rows = new List<AnnotationRow>();
        while (r.Read())
        {
            rows.Add(new AnnotationRow(
                r.GetInt32(0), r.GetInt32(1), r.GetInt32(2),
                r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
                r.GetDouble(5), r.GetDouble(6), r.GetDouble(7), r.GetDouble(8),
                r.IsDBNull(9) ? null : r.GetString(9), r.GetDouble(10),
                r.IsDBNull(11) ? null : r.GetString(11), r.GetInt32(12)));
        }

        return rows;
    });

    public static List<BookmarkRow> Bookmarks() => Query(c =>
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id, SheetId, PageIndex, Name FROM Bookmark ORDER BY Id";
        using var r = cmd.ExecuteReader();
        var rows = new List<BookmarkRow>();
        while (r.Read())
        {
            rows.Add(new BookmarkRow(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.IsDBNull(3) ? null : r.GetString(3)));
        }

        return rows;
    });

    public static List<SheetRow> Sheets() => Query(c =>
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id, FolderId, Title, FileName, PageCount, LastViewedPageIndex FROM Sheet ORDER BY Id";
        using var r = cmd.ExecuteReader();
        var rows = new List<SheetRow>();
        while (r.Read())
        {
            rows.Add(new SheetRow(
                r.GetInt32(0), r.IsDBNull(1) ? null : r.GetInt32(1), r.GetString(2), r.GetString(3),
                r.GetInt32(4), r.GetInt32(5)));
        }

        return rows;
    });

    /// <summary>Polls until <paramref name="condition"/> holds for the rows (DB writes are async in the app).</summary>
    public static List<AnnotationRow> WaitForAnnotations(Func<List<AnnotationRow>, bool> condition, string because, int timeoutSeconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        List<AnnotationRow> rows;
        do
        {
            rows = Annotations();
            if (condition(rows))
            {
                return rows;
            }

            Thread.Sleep(700);
        }
        while (DateTime.UtcNow < deadline);

        Assert.Fail($"Timed out waiting for annotations: {because}. Current rows: {string.Join(" | ", rows)}");
        return rows;
    }

    /// <summary>Inserts a Sheet row into a pulled copy of the DB (used by the "seed" import mode; app must be stopped).</summary>
    public static void InsertSheetOffline(string localDbPath, string title, string fileName)
    {
        using var connection = new SqliteConnection($"Data Source={localDbPath};Pooling=False");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO Sheet (FolderId, Title, FileName, PageCount, LastViewedPageIndex, DateAdded) VALUES (NULL, $t, $f, 0, 0, $d)";
        cmd.Parameters.AddWithValue("$t", title);
        cmd.Parameters.AddWithValue("$f", fileName);
        cmd.Parameters.AddWithValue("$d", DateTime.UtcNow.Ticks);
        cmd.ExecuteNonQuery();
    }
}
