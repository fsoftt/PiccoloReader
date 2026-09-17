using SQLite;

namespace PiccoloReader.Core.Data.Models;

public class Folder
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime DateAdded { get; set; }
}
