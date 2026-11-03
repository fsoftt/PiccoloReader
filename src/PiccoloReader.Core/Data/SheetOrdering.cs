using SQLite;

namespace PiccoloReader.Core.Data;

internal static class SheetOrdering
{
    // Position that appends a sheet at the end of a folder (root = null).
    public static Task<int> NextSortOrderAsync(SQLiteAsyncConnection connection, int? folderId) =>
        connection.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM Sheet WHERE FolderId IS ?", folderId);
}
