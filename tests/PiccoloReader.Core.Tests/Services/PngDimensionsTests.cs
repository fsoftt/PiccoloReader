using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests.Services;

public class PngDimensionsTests
{
    private static byte[] PngHeader(int width, int height) => new byte[]
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
        (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height,
    };

    [Fact]
    public void TryRead_ValidHeader_ReturnsDimensions()
    {
        Assert.True(PngDimensions.TryRead(PngHeader(1080, 1527), out var width, out var height));
        Assert.Equal(1080, width);
        Assert.Equal(1527, height);
    }

    [Fact]
    public void TryRead_NotAPng_ReturnsFalse()
    {
        Assert.False(PngDimensions.TryRead(new byte[] { 1, 2, 3 }, out _, out _));
        Assert.False(PngDimensions.TryRead(null, out _, out _));
    }
}
