using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests.Services;

public class FolderNameNormalizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    [InlineData("​")]
    [InlineData(" ​﻿ ")]
    [InlineData("ㅤ")]
    [InlineData("  ")]
    public void Normalize_BlankOrInvisibleName_ReturnsNull(string? name)
    {
        Assert.Null(FolderNameNormalizer.Normalize(name));
    }

    [Theory]
    [InlineData("  Bach  ", "Bach")]
    [InlineData("​Mozart​", "Mozart")]
    [InlineData("Études", "Études")]
    public void Normalize_VisibleName_ReturnsCleanedName(string name, string expected)
    {
        Assert.Equal(expected, FolderNameNormalizer.Normalize(name));
    }
}
