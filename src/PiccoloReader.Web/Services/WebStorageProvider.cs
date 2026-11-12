using PiccoloReader.Core.Services;

namespace PiccoloReader.Web.Services;

// DatabasePath is a file in Emscripten's in-memory FS; PersistentDatabase mirrors
// it to OPFS. PDFs live in OPFS under SheetsDirectory (see pdf.js).
public sealed class WebStorageProvider : IAppStorageProvider
{
    public const string DatabaseFileName = "piccolo.db3";

    public string DatabasePath => "/" + DatabaseFileName;

    public string SheetsDirectory => "sheets";
}
