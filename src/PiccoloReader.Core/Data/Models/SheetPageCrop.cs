using SQLite;

namespace PiccoloReader.Core.Data.Models;

// A page's crop, normalized (0-1) to the full page. One row per cropped
// (sheet, page); pages without a row are shown whole.
public class SheetPageCrop
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int SheetId { get; set; }

    public int PageIndex { get; set; }

    public double Left { get; set; }

    public double Top { get; set; }

    public double Right { get; set; }

    public double Bottom { get; set; }
}
