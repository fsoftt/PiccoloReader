using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests;

public class FigureCopyTests
{
    private static Annotation Figure(double x, double y, double w = 0.1, double h = 0.05) => new()
    {
        SheetId = 3,
        PageIndex = 2,
        Type = AnnotationType.Icon,
        IconKey = "slurAbove",
        X = x,
        Y = y,
        Width = w,
        Height = h,
        ColorHex = "#112233",
        StrokeWidth = 0.004,
        Points = "[1]",
        CoordinateSpace = AnnotationCoordinateSpace.Page,
        SyncId = "original"
    };

    [Fact]
    public void Copy_KeepsShapeAndGetsNewIdentity()
    {
        var source = Figure(0.2, 0.3);
        var copy = FigureCopy.Copy(source);

        Assert.NotEqual(source.SyncId, copy.SyncId);
        Assert.Equal(0, copy.Id);
        Assert.Equal(source.SheetId, copy.SheetId);
        Assert.Equal(source.PageIndex, copy.PageIndex);
        Assert.Equal(source.IconKey, copy.IconKey);
        Assert.Equal(source.Width, copy.Width);
        Assert.Equal(source.Height, copy.Height);
        Assert.Equal(source.ColorHex, copy.ColorHex);
        Assert.Equal(source.StrokeWidth, copy.StrokeWidth);
        Assert.Equal(source.Points, copy.Points);
        Assert.Equal(source.CoordinateSpace, copy.CoordinateSpace);
    }

    [Fact]
    public void Copy_OffsetsDownRight()
    {
        var copy = FigureCopy.Copy(Figure(0.2, 0.3));

        Assert.Equal(0.2 + FigureCopy.Offset, copy.X, 6);
        Assert.Equal(0.3 + FigureCopy.Offset, copy.Y, 6);
    }

    [Fact]
    public void Copy_NoRoomRightOrBelow_OffsetsOtherWay()
    {
        var copy = FigureCopy.Copy(Figure(0.89, 0.94));

        Assert.Equal(0.89 - FigureCopy.Offset, copy.X, 6);
        Assert.Equal(0.94 - FigureCopy.Offset, copy.Y, 6);
    }

    [Fact]
    public void Copy_RespectsCrop()
    {
        var copy = FigureCopy.Copy(Figure(0.4, 0.4), left: 0.2, top: 0.2, right: 0.52, bottom: 0.46);

        Assert.True(copy.X >= 0.2 && copy.X + copy.Width <= 0.52 + 1e-9);
        Assert.True(copy.Y >= 0.2 && copy.Y + copy.Height <= 0.46 + 1e-9);
    }

    [Fact]
    public void Copy_BoxLargerThanBounds_StaysInsideAsMuchAsPossible()
    {
        var copy = FigureCopy.Copy(Figure(0, 0, 1.0, 1.0));

        Assert.Equal(0, copy.X, 6);
        Assert.Equal(0, copy.Y, 6);
    }
}
