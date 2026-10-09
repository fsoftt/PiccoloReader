using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests.Services;

public class AnnotationCoordinateMigratorTests
{
    private static Annotation LegacyIcon(double x, double y, double width, double height) => new()
    {
        Type = AnnotationType.Icon,
        IconKey = "dynamicForte",
        X = x,
        Y = y,
        Width = width,
        Height = height,
        CoordinateSpace = AnnotationCoordinateSpace.Legacy
    };

    private static Annotation LegacyStroke(double strokeWidth, params StrokePoint[] points) => new()
    {
        Type = AnnotationType.Stroke,
        ColorHex = "#000000",
        StrokeWidth = strokeWidth,
        Points = AnnotationService.SerializePoints(points),
        CoordinateSpace = AnnotationCoordinateSpace.Legacy
    };

    [Fact]
    public void MigrateToPageSpace_TallPageInPortraitContainer_RemovesLetterboxOffset()
    {
        // 400x800 container, page 400x600 -> page spans y 0.125..0.875.
        var icon = LegacyIcon(x: 0.5, y: 0.5, width: 0.1, height: 0.075);

        var migrated = AnnotationCoordinateMigrator.MigrateToPageSpace(icon, 1.5, 400, 800);

        Assert.True(migrated);
        Assert.Equal(AnnotationCoordinateSpace.Page, icon.CoordinateSpace);
        Assert.Equal(0.5, icon.X, 6);
        Assert.Equal(0.5, icon.Y, 6);
        Assert.Equal(0.1, icon.Width, 6);
        Assert.Equal(0.1, icon.Height, 6);
    }

    [Fact]
    public void MigrateToPageSpace_IconAtPageCorners_MapsToZeroAndOne()
    {
        // Page top-left is at container (0, 0.125); bottom-right at (1, 0.875).
        var icon = LegacyIcon(x: 0, y: 0.125, width: 1, height: 0.75);

        AnnotationCoordinateMigrator.MigrateToPageSpace(icon, 1.5, 400, 800);

        Assert.Equal(0, icon.X, 6);
        Assert.Equal(0, icon.Y, 6);
        Assert.Equal(1, icon.Width, 6);
        Assert.Equal(1, icon.Height, 6);
    }

    [Fact]
    public void MigrateToPageSpace_WidePage_ScalesHeightByPageHeightFraction()
    {
        // Page 400x200 in a 400x800 container: y 0.375..0.625, height fraction 0.25.
        var icon = LegacyIcon(x: 0.25, y: 0.5, width: 0.2, height: 0.05);

        AnnotationCoordinateMigrator.MigrateToPageSpace(icon, 0.5, 400, 800);

        Assert.Equal(0.25, icon.X, 6);
        Assert.Equal(0.5, icon.Y, 6);
        Assert.Equal(0.2, icon.Width, 6);
        Assert.Equal(0.2, icon.Height, 6);
    }

    [Fact]
    public void MigrateToPageSpace_VeryTallPage_IsWidthLimitedByHeight()
    {
        // Page aspect 2.5 in 400x800: page 320x800, x spans 0.1..0.9.
        var icon = LegacyIcon(x: 0.1, y: 0, width: 0.4, height: 0.5);

        AnnotationCoordinateMigrator.MigrateToPageSpace(icon, 2.5, 400, 800);

        Assert.Equal(0, icon.X, 6);
        Assert.Equal(0, icon.Y, 6);
        Assert.Equal(0.5, icon.Width, 6);
        Assert.Equal(0.5, icon.Height, 6);
    }

    [Fact]
    public void MigrateToPageSpace_LandscapeContainer_UsesPortraitReference()
    {
        var fromPortrait = LegacyIcon(0.3, 0.6, 0.1, 0.1);
        var fromLandscape = LegacyIcon(0.3, 0.6, 0.1, 0.1);

        AnnotationCoordinateMigrator.MigrateToPageSpace(fromPortrait, 1.4, 400, 800);
        AnnotationCoordinateMigrator.MigrateToPageSpace(fromLandscape, 1.4, 800, 400);

        Assert.Equal(fromPortrait.X, fromLandscape.X, 9);
        Assert.Equal(fromPortrait.Y, fromLandscape.Y, 9);
        Assert.Equal(fromPortrait.Width, fromLandscape.Width, 9);
        Assert.Equal(fromPortrait.Height, fromLandscape.Height, 9);
    }

    [Fact]
    public void MigrateToPageSpace_Stroke_ConvertsPointsAndStrokeWidth()
    {
        // Page aspect 2.5 in 400x800: page x 0.1..0.9 (width 0.8), full height.
        var stroke = LegacyStroke(0.008, new StrokePoint(0.1, 0), new StrokePoint(0.5, 0.5), new StrokePoint(0.9, 1));

        var migrated = AnnotationCoordinateMigrator.MigrateToPageSpace(stroke, 2.5, 400, 800);

        Assert.True(migrated);
        var points = AnnotationService.DeserializePoints(stroke.Points);
        Assert.Equal(3, points.Count);
        Assert.Equal(0, points[0].X, 6);
        Assert.Equal(0, points[0].Y, 6);
        Assert.Equal(0.5, points[1].X, 6);
        Assert.Equal(0.5, points[1].Y, 6);
        Assert.Equal(1, points[2].X, 6);
        Assert.Equal(1, points[2].Y, 6);
        Assert.Equal(0.01, stroke.StrokeWidth, 9);
        Assert.Equal(AnnotationCoordinateSpace.Page, stroke.CoordinateSpace);
    }

    [Fact]
    public void MigrateToPageSpace_Stroke_LeavesIconGeometryUntouched()
    {
        var stroke = LegacyStroke(0.01, new StrokePoint(0.2, 0.3), new StrokePoint(0.4, 0.5));

        AnnotationCoordinateMigrator.MigrateToPageSpace(stroke, 1.5, 400, 800);

        Assert.Equal(0, stroke.X);
        Assert.Equal(0, stroke.Width);
    }

    [Fact]
    public void MigrateToPageSpace_PageSpaceAnnotation_IsLeftAlone()
    {
        var icon = LegacyIcon(0.5, 0.5, 0.1, 0.075);
        icon.CoordinateSpace = AnnotationCoordinateSpace.Page;

        var migrated = AnnotationCoordinateMigrator.MigrateToPageSpace(icon, 1.5, 400, 800);

        Assert.False(migrated);
        Assert.Equal(0.5, icon.Y);
        Assert.Equal(0.075, icon.Height);
    }

    [Fact]
    public void MigrateToPageSpace_RunTwice_ConvertsOnlyOnce()
    {
        var icon = LegacyIcon(0.5, 0.3, 0.1, 0.075);

        Assert.True(AnnotationCoordinateMigrator.MigrateToPageSpace(icon, 1.5, 400, 800));
        var y = icon.Y;
        var height = icon.Height;

        Assert.False(AnnotationCoordinateMigrator.MigrateToPageSpace(icon, 1.5, 400, 800));
        Assert.Equal(y, icon.Y);
        Assert.Equal(height, icon.Height);
    }

    [Theory]
    [InlineData(0, 800, 800)]
    [InlineData(400, 0, 800)]
    [InlineData(400, 800, 0)]
    [InlineData(400, 800, -1)]
    public void MigrateToPageSpace_UnusableInputs_StaysLegacy(double width, double height, double aspect)
    {
        var icon = LegacyIcon(0.5, 0.5, 0.1, 0.1);

        var migrated = AnnotationCoordinateMigrator.MigrateToPageSpace(icon, aspect, width, height);

        Assert.False(migrated);
        Assert.Equal(AnnotationCoordinateSpace.Legacy, icon.CoordinateSpace);
        Assert.Equal(0.5, icon.Y);
    }

    [Fact]
    public void MigrateToPageSpace_StrokeWithoutPoints_StillMarkedMigrated()
    {
        var stroke = LegacyStroke(0.01);
        stroke.Points = null;

        var migrated = AnnotationCoordinateMigrator.MigrateToPageSpace(stroke, 1.5, 400, 800);

        Assert.True(migrated);
        Assert.Null(stroke.Points);
    }
}
