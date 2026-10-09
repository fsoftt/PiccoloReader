namespace PiccoloReader.Core.Services;

public static class PngDimensions
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    // Reads width/height from the IHDR chunk, which the PNG spec requires
    // to be the first chunk - bytes 16-23, big-endian - without decoding
    // the image.
    public static bool TryRead(byte[]? png, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (png is null || png.Length < 24 || !png.AsSpan(0, 8).SequenceEqual(Signature))
        {
            return false;
        }

        width = ReadBigEndianInt32(png, 16);
        height = ReadBigEndianInt32(png, 20);
        return width > 0 && height > 0;
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
}
