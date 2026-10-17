namespace PiccoloReader.Core.Services;

// Small cached copies of rendered pages, shown at once while a score opens
// and its full-size page is still rendering. Images are PNG so their
// dimensions can be read like those of a normal render.
public interface IPageThumbnailStore
{
    Task<byte[]?> TryLoadAsync(int sheetId, int pageIndex);

    // Downscales and stores the page image, replacing any previous one.
    // Failures are swallowed: a missing thumbnail only costs the blank wait.
    Task SaveAsync(int sheetId, int pageIndex, byte[] pageImageBytes);

    Task DeleteForSheetAsync(int sheetId);
}
