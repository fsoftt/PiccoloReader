# Pencil Annotations + Eraser Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user pick Pencil (freehand strokes, chosen color/width) or Eraser (whole-object delete by dragging over icons or strokes) from the Sheet Viewer's tool panel, alongside the existing Music Icons picker — a three-tab panel replacing today's single-purpose one.

**Architecture:** Extends the existing `Annotation` table (not a new table) with `Type`/`ColorHex`/`StrokeWidth`/`Points` columns for strokes, auto-migrated by sqlite-net-pcl the same way `Sheet.LastViewedPageIndex` was added. `AnnotationService` gains `AddStrokeAsync` alongside the existing `AddIconAsync`. The tool panel becomes a 3-tab selector (Music Icons/Pencil/Eraser) that never auto-closes on tab switch, plus a toolbar X icon (visible only while Pencil/Eraser is active) for one-tap deactivation. Pencil drawing uses `CommunityToolkit.Maui.Views.DrawingView` (already a project dependency) as an overlay inside `PageContainer`, interactive only while Pencil is active — it inherits the page's zoom/pan transform for free and hands off each finished stroke's points via its `DrawingLineCompleted` event. Eraser has no equivalent library control, so it extends the existing `OnPanUpdated` handler with a new branch, hit-testing along the drag path (bounding-box for icons, point-to-polyline distance for strokes) and deleting whole objects on hit.

**Tech Stack:** .NET MAUI (Android only, same scope as prior plans), CommunityToolkit.Mvvm, sqlite-net-pcl, SkiaSharp (existing `AnnotationCanvas`), `CommunityToolkit.Maui.Views.DrawingView` (package already referenced, `CommunityToolkit.Maui` 13.0.0 — no new dependency).

**Spec:** [`docs/superpowers/specs/2026-09-16-pencil-annotations-eraser-design.md`](../specs/2026-09-16-pencil-annotations-eraser-design.md)

## Global Constraints

- Android only — no iOS-specific work, matching every prior plan.
- No undo/redo — every stroke add and eraser delete commits to SQLite immediately, deferred to Plan 5 per the spec.
- No selecting/moving/resizing a placed stroke — only the Eraser removes one, whole, never partial.
- One task = one PR. Wait for explicit merge confirmation before starting the next task's branch.
- Every PR that changes on-screen behavior needs on-device verification (build for Android, exercise the flow, check logcat for crashes) before opening the PR.
- `DrawingView.LineWidth`/`DrawingView.LineColor` are **not** normalized — they're the control's own device-independent pixel width and a `Microsoft.Maui.Graphics.Color`. The app's own `PencilStrokeWidth` (normalized, 0.0–1.0 relative to page width) and `PencilColorHex` (string) are the values actually persisted; conversion to `DrawingView`'s units happens only at the point of feeding the control, never stored that way.

---

### Task 1: Data model — Annotation columns, StrokePoint, AnnotationService.AddStrokeAsync

**Files:**
- Modify: `src/PiccoloReader.Core/Data/Models/Annotation.cs`
- Create: `src/PiccoloReader.Core/Data/Models/StrokePoint.cs`
- Modify: `src/PiccoloReader.Core/Services/AnnotationService.cs`
- Test: `tests/PiccoloReader.Core.Tests/Services/AnnotationServiceTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks (first task of this plan).
- Produces (consumed by later tasks):
  - `PiccoloReader.Core.Data.Models.Annotation` — new `Type` (`string`), `ColorHex` (`string?`), `StrokeWidth` (`double`), `Points` (`string?`) properties, plus a computed `IsStroke` (`bool`) property.
  - `PiccoloReader.Core.Data.Models.AnnotationType` — static class with `Icon`/`Stroke` string constants.
  - `PiccoloReader.Core.Data.Models.StrokePoint` record — `X`, `Y` (`double`, normalized).
  - `AnnotationService.AddStrokeAsync(int sheetId, int pageIndex, string colorHex, double strokeWidth, IReadOnlyList<StrokePoint> points)`.
  - `AnnotationService.SerializePoints(IReadOnlyList<StrokePoint> points)` (`string`) and `AnnotationService.DeserializePoints(string? json)` (`IReadOnlyList<StrokePoint>`) — both `static`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/PiccoloReader.Core.Tests/Services/AnnotationServiceTests.cs`, after the existing `DeleteAnnotationAsync_RemovesRow` test, before the closing brace. Add `using PiccoloReader.Core.Data.Models;` to the top of the file first (needed for `StrokePoint`/`AnnotationType`):

```csharp
[Fact]
public async Task AddStrokeAsync_InsertsRetrievableStroke()
{
    var points = new List<StrokePoint> { new(0.1, 0.1), new(0.2, 0.2), new(0.3, 0.15) };

    var annotation = await _sut.AddStrokeAsync(sheetId: 1, pageIndex: 0, colorHex: "#FF0000", strokeWidth: 0.01, points: points);

    var loaded = await _sut.GetAnnotationsAsync(sheetId: 1, pageIndex: 0);

    Assert.Single(loaded);
    Assert.Equal(annotation.Id, loaded[0].Id);
    Assert.Equal(AnnotationType.Stroke, loaded[0].Type);
    Assert.True(loaded[0].IsStroke);
    Assert.Equal("#FF0000", loaded[0].ColorHex);
    Assert.Equal(0.01, loaded[0].StrokeWidth);

    var roundTrippedPoints = AnnotationService.DeserializePoints(loaded[0].Points);
    Assert.Equal(3, roundTrippedPoints.Count);
    Assert.Equal(0.1, roundTrippedPoints[0].X);
    Assert.Equal(0.1, roundTrippedPoints[0].Y);
    Assert.Equal(0.3, roundTrippedPoints[2].X);
}

[Fact]
public async Task AddIconAsync_SetsTypeToIcon()
{
    var annotation = await _sut.AddIconAsync(1, 0, "dynamicForte", 0.1, 0.1, 0.1, 0.1);

    Assert.Equal(AnnotationType.Icon, annotation.Type);
    Assert.False(annotation.IsStroke);
}

[Fact]
public void Annotation_WithNullType_IsTreatedAsIcon()
{
    var annotation = new Annotation { Type = null! };

    Assert.False(annotation.IsStroke);
}

[Fact]
public void SerializePoints_DeserializePoints_RoundTrips()
{
    var points = new List<StrokePoint> { new(0.0, 0.0), new(1.0, 1.0) };

    var json = AnnotationService.SerializePoints(points);
    var result = AnnotationService.DeserializePoints(json);

    Assert.Equal(2, result.Count);
    Assert.Equal(points[0], result[0]);
    Assert.Equal(points[1], result[1]);
}

[Fact]
public void DeserializePoints_NullOrEmpty_ReturnsEmptyList()
{
    Assert.Empty(AnnotationService.DeserializePoints(null));
    Assert.Empty(AnnotationService.DeserializePoints(""));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter "FullyQualifiedName~AnnotationServiceTests|FullyQualifiedName~Annotation_WithNullType"`
Expected: FAIL to compile — `StrokePoint`, `AnnotationType`, `AddStrokeAsync`, `SerializePoints`/`DeserializePoints`, `IsStroke` don't exist yet.

- [ ] **Step 3: Write StrokePoint**

```csharp
// src/PiccoloReader.Core/Data/Models/StrokePoint.cs
namespace PiccoloReader.Core.Data.Models;

public record StrokePoint(double X, double Y);
```

- [ ] **Step 4: Update Annotation model**

Full new content of `src/PiccoloReader.Core/Data/Models/Annotation.cs`:

```csharp
using SQLite;

namespace PiccoloReader.Core.Data.Models;

public class Annotation
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int SheetId { get; set; }

    public int PageIndex { get; set; }

    public string Type { get; set; } = AnnotationType.Icon;

    public string IconKey { get; set; } = string.Empty;

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public string? ColorHex { get; set; }

    public double StrokeWidth { get; set; }

    public string? Points { get; set; }

    public DateTime CreatedAt { get; set; }

    // Existing rows predate the Type column and read back with Type
    // null/empty from SQLite (sqlite-net-pcl overwrites the field
    // initializer's default with the column's actual NULL value on
    // read, so the initializer alone doesn't cover old rows) - treating
    // anything that isn't explicitly "Stroke" as an icon means old rows
    // keep working with no backfill migration needed.
    public bool IsStroke => Type == AnnotationType.Stroke;
}

public static class AnnotationType
{
    public const string Icon = "Icon";
    public const string Stroke = "Stroke";
}
```

- [ ] **Step 5: Update AnnotationService**

Full new content of `src/PiccoloReader.Core/Services/AnnotationService.cs`:

```csharp
using System.Text.Json;
using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.Services;

public class AnnotationService
{
    private readonly AppDatabase _database;

    public AnnotationService(AppDatabase database)
    {
        _database = database;
    }

    public Task<List<Annotation>> GetAnnotationsAsync(int sheetId, int pageIndex) =>
        _database.Connection.Table<Annotation>()
            .Where(a => a.SheetId == sheetId && a.PageIndex == pageIndex)
            .ToListAsync();

    public async Task<Annotation> AddIconAsync(int sheetId, int pageIndex, string iconKey, double x, double y, double width, double height)
    {
        var annotation = new Annotation
        {
            SheetId = sheetId,
            PageIndex = pageIndex,
            Type = AnnotationType.Icon,
            IconKey = iconKey,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            CreatedAt = DateTime.UtcNow
        };

        await _database.Connection.InsertAsync(annotation);
        return annotation;
    }

    public async Task<Annotation> AddStrokeAsync(int sheetId, int pageIndex, string colorHex, double strokeWidth, IReadOnlyList<StrokePoint> points)
    {
        var annotation = new Annotation
        {
            SheetId = sheetId,
            PageIndex = pageIndex,
            Type = AnnotationType.Stroke,
            ColorHex = colorHex,
            StrokeWidth = strokeWidth,
            Points = SerializePoints(points),
            CreatedAt = DateTime.UtcNow
        };

        await _database.Connection.InsertAsync(annotation);
        return annotation;
    }

    public Task UpdateAnnotationAsync(Annotation annotation) =>
        _database.Connection.UpdateAsync(annotation);

    public Task DeleteAnnotationAsync(Annotation annotation) =>
        _database.Connection.DeleteAsync(annotation);

    public static string SerializePoints(IReadOnlyList<StrokePoint> points) =>
        JsonSerializer.Serialize(points);

    public static IReadOnlyList<StrokePoint> DeserializePoints(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return Array.Empty<StrokePoint>();
        }

        return JsonSerializer.Deserialize<List<StrokePoint>>(json) ?? new List<StrokePoint>();
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
Expected: all tests pass (total count goes from 51 to 56).

- [ ] **Step 7: Build for Android to verify the new columns compile cleanly**

Run: `dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 8: On-device sanity check**

Run: `dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android -t:Run`
Open the existing test sheet with its previously-placed icon (from earlier plans) — confirm it still loads and renders correctly, proving the new nullable columns didn't break reading pre-existing rows. Check logcat for crashes.

- [ ] **Step 9: Commit**

```bash
git add src/PiccoloReader.Core/Data/Models/Annotation.cs src/PiccoloReader.Core/Data/Models/StrokePoint.cs src/PiccoloReader.Core/Services/AnnotationService.cs tests/PiccoloReader.Core.Tests/Services/AnnotationServiceTests.cs
git commit -m "feat: extend Annotation model for strokes, add AnnotationService.AddStrokeAsync"
```

---

### Task 2: Eraser hit-test math — StrokeHitTester

**Files:**
- Create: `src/PiccoloReader.Core/Services/StrokeHitTester.cs`
- Test: `tests/PiccoloReader.Core.Tests/Services/StrokeHitTesterTests.cs`

**Interfaces:**
- Consumes: `StrokePoint` (Task 1).
- Produces (consumed by Task 5 — Eraser mechanics):
  - `StrokeHitTester.DistanceToPolyline(double pointX, double pointY, IReadOnlyList<StrokePoint> points)` (`double`) — minimum distance from a point to the nearest segment of the polyline; `double.MaxValue` for an empty list.

This task is independent of Task 1's data-model specifics beyond the `StrokePoint` shape, and independent of every other task — pure geometry, no SQLite/MAUI dependency.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/PiccoloReader.Core.Tests/Services/StrokeHitTesterTests.cs
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests.Services;

public class StrokeHitTesterTests
{
    [Fact]
    public void DistanceToPolyline_PointOnSegment_ReturnsZero()
    {
        var points = new List<StrokePoint> { new(0, 0), new(1, 0) };

        var distance = StrokeHitTester.DistanceToPolyline(0.5, 0, points);

        Assert.Equal(0, distance, precision: 10);
    }

    [Fact]
    public void DistanceToPolyline_PointAwayFromSegment_ReturnsPerpendicularDistance()
    {
        var points = new List<StrokePoint> { new(0, 0), new(1, 0) };

        var distance = StrokeHitTester.DistanceToPolyline(0.5, 0.3, points);

        Assert.Equal(0.3, distance, precision: 10);
    }

    [Fact]
    public void DistanceToPolyline_PointPastSegmentEnd_ReturnsDistanceToNearestEndpoint()
    {
        var points = new List<StrokePoint> { new(0, 0), new(1, 0) };

        var distance = StrokeHitTester.DistanceToPolyline(2, 0, points);

        Assert.Equal(1, distance, precision: 10);
    }

    [Fact]
    public void DistanceToPolyline_MultiSegmentPolyline_ReturnsMinimumAcrossSegments()
    {
        var points = new List<StrokePoint> { new(0, 0), new(1, 0), new(1, 1) };

        var distance = StrokeHitTester.DistanceToPolyline(1.1, 0.5, points);

        Assert.Equal(0.1, distance, precision: 10);
    }

    [Fact]
    public void DistanceToPolyline_SinglePoint_ReturnsDistanceToThatPoint()
    {
        var points = new List<StrokePoint> { new(0, 0) };

        var distance = StrokeHitTester.DistanceToPolyline(3, 4, points);

        Assert.Equal(5, distance, precision: 10);
    }

    [Fact]
    public void DistanceToPolyline_EmptyList_ReturnsMaxValue()
    {
        var distance = StrokeHitTester.DistanceToPolyline(0, 0, new List<StrokePoint>());

        Assert.Equal(double.MaxValue, distance);
    }

    [Fact]
    public void DistanceToPolyline_ZeroLengthSegment_ReturnsDistanceToThatPoint()
    {
        var points = new List<StrokePoint> { new(2, 2), new(2, 2) };

        var distance = StrokeHitTester.DistanceToPolyline(2, 5, points);

        Assert.Equal(3, distance, precision: 10);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter "FullyQualifiedName~StrokeHitTesterTests"`
Expected: FAIL to compile — `StrokeHitTester` doesn't exist yet.

- [ ] **Step 3: Write StrokeHitTester**

```csharp
// src/PiccoloReader.Core/Services/StrokeHitTester.cs
using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.Services;

public static class StrokeHitTester
{
    public static double DistanceToPolyline(double pointX, double pointY, IReadOnlyList<StrokePoint> points)
    {
        if (points.Count == 0)
        {
            return double.MaxValue;
        }

        if (points.Count == 1)
        {
            return Distance(pointX, pointY, points[0].X, points[0].Y);
        }

        var minDistance = double.MaxValue;
        for (var i = 0; i < points.Count - 1; i++)
        {
            var distance = DistanceToSegment(pointX, pointY, points[i].X, points[i].Y, points[i + 1].X, points[i + 1].Y);
            minDistance = Math.Min(minDistance, distance);
        }

        return minDistance;
    }

    private static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        var abx = bx - ax;
        var aby = by - ay;
        var lengthSquared = abx * abx + aby * aby;

        if (lengthSquared == 0)
        {
            return Distance(px, py, ax, ay);
        }

        var t = ((px - ax) * abx + (py - ay) * aby) / lengthSquared;
        t = Math.Clamp(t, 0, 1);

        var closestX = ax + t * abx;
        var closestY = ay + t * aby;

        return Distance(px, py, closestX, closestY);
    }

    private static double Distance(double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
Expected: all tests pass (total count goes from 56 to 63).

- [ ] **Step 5: Commit**

```bash
git add src/PiccoloReader.Core/Services/StrokeHitTester.cs tests/PiccoloReader.Core.Tests/Services/StrokeHitTesterTests.cs
git commit -m "feat: add StrokeHitTester for eraser point-to-polyline distance"
```

---

### Task 3: Tool panel — three-tab selector, ActiveTool state, toolbar X icon

**Files:**
- Create: `src/PiccoloReader.Core/ViewModels/AnnotationTool.cs`
- Modify: `src/PiccoloReader.Core/ViewModels/SheetViewerViewModel.cs`
- Modify: `src/PiccoloReader/Views/SheetViewerPage.xaml`
- Modify: `src/PiccoloReader/Views/SheetViewerPage.xaml.cs`
- Test: `tests/PiccoloReader.Core.Tests/ViewModels/SheetViewerViewModelTests.cs`

**Interfaces:**
- Consumes: nothing from Tasks 1–2 directly (this task is purely the tab/toolbar UI shell; Pencil/Eraser tabs get real content in Tasks 4–5).
- Produces (consumed by Tasks 4–5):
  - `PiccoloReader.Core.ViewModels.AnnotationTool` enum — `MusicIcons`, `Pencil`, `Eraser`.
  - `SheetViewerViewModel.ActiveTool` (`AnnotationTool`, `[ObservableProperty]`, defaults to `MusicIcons`).
  - `SheetViewerViewModel.IsDrawingToolActive` (`bool`, computed: `ActiveTool != AnnotationTool.MusicIcons`, notified via `[NotifyPropertyChangedFor]` on `ActiveTool`).
  - Named XAML elements `PencilSection`/`EraserSection` (`VerticalStackLayout`, initially near-empty placeholders — Tasks 4/5 fill them in) that later tasks add real controls into, and `MusicIconsSection` wrapping today's existing category grid unchanged.

- [ ] **Step 1: Write the failing ViewModel test**

Add to `tests/PiccoloReader.Core.Tests/ViewModels/SheetViewerViewModelTests.cs`, after the existing `DeleteSelectedAnnotationAsync_RemovesFromCollectionAndPersistence` test, before the closing brace:

```csharp
[Fact]
public async Task ActiveTool_DefaultsToMusicIcons()
{
    var sheet = await InsertSheetAsync(pageCount: 3);
    await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

    Assert.Equal(AnnotationTool.MusicIcons, _sut.ActiveTool);
    Assert.False(_sut.IsDrawingToolActive);
}

[Fact]
public void IsDrawingToolActive_TrueWhenPencilOrEraserActive()
{
    _sut.ActiveTool = AnnotationTool.Pencil;
    Assert.True(_sut.IsDrawingToolActive);

    _sut.ActiveTool = AnnotationTool.Eraser;
    Assert.True(_sut.IsDrawingToolActive);

    _sut.ActiveTool = AnnotationTool.MusicIcons;
    Assert.False(_sut.IsDrawingToolActive);
}
```

Add `using PiccoloReader.Core.ViewModels;` to the top of the test file if not already present (it already is, from the existing `SheetViewerViewModel` usage).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter "FullyQualifiedName~ActiveTool|FullyQualifiedName~IsDrawingToolActive"`
Expected: FAIL to compile — `AnnotationTool`, `ActiveTool`, `IsDrawingToolActive` don't exist yet.

- [ ] **Step 3: Write the AnnotationTool enum**

```csharp
// src/PiccoloReader.Core/ViewModels/AnnotationTool.cs
namespace PiccoloReader.Core.ViewModels;

public enum AnnotationTool
{
    MusicIcons,
    Pencil,
    Eraser
}
```

- [ ] **Step 4: Add ActiveTool/IsDrawingToolActive to SheetViewerViewModel**

Add near the other `[ObservableProperty]` fields (e.g. after `_selectedAnnotation`):

```csharp
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(IsDrawingToolActive))]
private AnnotationTool _activeTool = AnnotationTool.MusicIcons;

public bool IsDrawingToolActive => ActiveTool != AnnotationTool.MusicIcons;
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
Expected: all tests pass (total count goes from 63 to 65).

- [ ] **Step 6: Restructure SheetViewerPage.xaml — tab row, three sections, toolbar X icon**

Replace the `<ContentPage.ToolbarItems>` block:

```xml
<ContentPage.ToolbarItems>
    <ToolbarItem Clicked="OnToggleToolPanelClicked">
        <ToolbarItem.IconImageSource>
            <FontImageSource Glyph="&#xE3C9;" FontFamily="MaterialOutlined" Size="24" Color="White" />
        </ToolbarItem.IconImageSource>
    </ToolbarItem>
    <ToolbarItem Clicked="OnDeactivateToolClicked" IsVisible="{Binding IsDrawingToolActive}">
        <ToolbarItem.IconImageSource>
            <FontImageSource Glyph="&#xE14C;" FontFamily="MaterialOutlined" Size="24" Color="White" />
        </ToolbarItem.IconImageSource>
    </ToolbarItem>
</ContentPage.ToolbarItems>
```

(`&#xE14C;` is the verified "Close" glyph in this project's `MaterialOutlined` font — confirmed via reflection against the actual `UraniumUI.Icons.MaterialSymbols` assembly during PR #38, not assumed.)

Replace the entire `ToolPanel` `Border` (everything from `<Border x:Name="ToolPanel" ...>` to its closing `</Border>`) with:

```xml
<Border
    x:Name="ToolPanel"
    IsVisible="False"
    WidthRequest="260"
    HorizontalOptions="End"
    VerticalOptions="Fill"
    BackgroundColor="{AppThemeBinding Light={StaticResource Gray100}, Dark={StaticResource Gray600}}"
    StrokeThickness="0">
    <Grid RowDefinitions="Auto,*">

        <HorizontalStackLayout Grid.Row="0" Padding="12,12,12,4" Spacing="8">
            <Border x:Name="MusicIconsTabButton" WidthRequest="52" HeightRequest="44" Padding="4" StrokeThickness="1">
                <Border.GestureRecognizers>
                    <TapGestureRecognizer Tapped="OnToolTabTapped" CommandParameter="MusicIcons" />
                </Border.GestureRecognizers>
                <Image HorizontalOptions="Center" VerticalOptions="Center">
                    <Image.Source>
                        <FontImageSource Glyph="&#xE405;" FontFamily="MaterialOutlined" Size="22" Color="{StaticResource Black}" />
                    </Image.Source>
                </Image>
            </Border>
            <Border x:Name="PencilTabButton" WidthRequest="52" HeightRequest="44" Padding="4" StrokeThickness="1">
                <Border.GestureRecognizers>
                    <TapGestureRecognizer Tapped="OnToolTabTapped" CommandParameter="Pencil" />
                </Border.GestureRecognizers>
                <Image HorizontalOptions="Center" VerticalOptions="Center">
                    <Image.Source>
                        <FontImageSource Glyph="&#xE3C9;" FontFamily="MaterialOutlined" Size="22" Color="{StaticResource Black}" />
                    </Image.Source>
                </Image>
            </Border>
            <Border x:Name="EraserTabButton" WidthRequest="52" HeightRequest="44" Padding="4" StrokeThickness="1">
                <Border.GestureRecognizers>
                    <TapGestureRecognizer Tapped="OnToolTabTapped" CommandParameter="Eraser" />
                </Border.GestureRecognizers>
                <Image HorizontalOptions="Center" VerticalOptions="Center">
                    <Image.Source>
                        <FontImageSource Glyph="&#xE14C;" FontFamily="MaterialOutlined" Size="22" Color="{StaticResource Black}" />
                    </Image.Source>
                </Image>
            </Border>
        </HorizontalStackLayout>

        <ScrollView Grid.Row="1">
            <Grid Padding="12">

                <VerticalStackLayout x:Name="MusicIconsSection" Spacing="4">
                    <Label Text="Music Icons" FontAttributes="Bold" FontSize="16" Margin="0,0,0,8" />
                    <CollectionView ItemsSource="{Binding Source={x:Reference ThisPage}, Path=IconCategories}">
                        <CollectionView.ItemTemplate>
                            <DataTemplate>
                                <VerticalStackLayout Spacing="4" Margin="0,0,0,12">
                                    <Label Text="{Binding Name}" FontAttributes="Bold" FontSize="14" />
                                    <FlexLayout Wrap="Wrap" Direction="Row" BindableLayout.ItemsSource="{Binding Icons}">
                                        <BindableLayout.ItemTemplate>
                                            <DataTemplate>
                                                <Border
                                                    WidthRequest="52"
                                                    HeightRequest="52"
                                                    Margin="4"
                                                    Padding="4"
                                                    BackgroundColor="{StaticResource White}"
                                                    StrokeThickness="1">
                                                    <Border.GestureRecognizers>
                                                        <TapGestureRecognizer
                                                            Tapped="OnIconPickerTapped"
                                                            CommandParameter="{Binding Key}" />
                                                    </Border.GestureRecognizers>
                                                    <skia:SKCanvasView
                                                        WidthRequest="40"
                                                        HeightRequest="40"
                                                        HorizontalOptions="Center"
                                                        VerticalOptions="Center"
                                                        PaintSurface="OnIconGlyphPaintSurface" />
                                                </Border>
                                            </DataTemplate>
                                        </BindableLayout.ItemTemplate>
                                    </FlexLayout>
                                </VerticalStackLayout>
                            </DataTemplate>
                        </CollectionView.ItemTemplate>
                    </CollectionView>
                </VerticalStackLayout>

                <VerticalStackLayout x:Name="PencilSection" IsVisible="False" Spacing="8">
                    <Label Text="Pencil" FontAttributes="Bold" FontSize="16" />
                </VerticalStackLayout>

                <VerticalStackLayout x:Name="EraserSection" IsVisible="False" Spacing="8">
                    <Label Text="Eraser" FontAttributes="Bold" FontSize="16" />
                </VerticalStackLayout>

            </Grid>
        </ScrollView>
    </Grid>
</Border>
```

`MusicIconsSection`/`PencilSection`/`EraserSection` are siblings in the same `Grid` cell (Grid children overlap by default) — exactly one is visible at a time, toggled in code-behind. Tasks 4/5 add real controls inside `PencilSection`/`EraserSection`; this task only needs their section titles to exist so tab-switching has something visible to prove it works.

- [ ] **Step 7: Add tab-switching and deactivate handlers to SheetViewerPage.xaml.cs**

Add fields, near the other private fields:

```csharp
private static readonly Color TabActiveColor = (Color)Application.Current!.Resources["Primary"];
private static readonly Color TabInactiveColor = Colors.Transparent;
```

Add methods, anywhere in the class body (e.g. near `OnToggleToolPanelClicked`):

```csharp
private void OnToolTabTapped(object? sender, TappedEventArgs e)
{
    if (e.Parameter is not string toolName || !Enum.TryParse<AnnotationTool>(toolName, out var tool))
    {
        return;
    }

    _viewModel.ActiveTool = tool;
    UpdateToolSections();
}

private void OnDeactivateToolClicked(object? sender, EventArgs e)
{
    _viewModel.ActiveTool = AnnotationTool.MusicIcons;
    UpdateToolSections();
}

private void UpdateToolSections()
{
    MusicIconsSection.IsVisible = _viewModel.ActiveTool == AnnotationTool.MusicIcons;
    PencilSection.IsVisible = _viewModel.ActiveTool == AnnotationTool.Pencil;
    EraserSection.IsVisible = _viewModel.ActiveTool == AnnotationTool.Eraser;

    MusicIconsTabButton.BackgroundColor = _viewModel.ActiveTool == AnnotationTool.MusicIcons ? TabActiveColor : TabInactiveColor;
    PencilTabButton.BackgroundColor = _viewModel.ActiveTool == AnnotationTool.Pencil ? TabActiveColor : TabInactiveColor;
    EraserTabButton.BackgroundColor = _viewModel.ActiveTool == AnnotationTool.Eraser ? TabActiveColor : TabInactiveColor;
}
```

Add `using PiccoloReader.Core.ViewModels;` to the top of the file if not already present (it already is, from `SheetViewerViewModel`).

Call `UpdateToolSections()` once from `OnAppearing`, right after `await EnsureBravuraTypefaceLoadedAsync();`, so the panel starts in the correct state (Music Icons section visible, both tab buttons' highlight correct) even before the user taps anything.

- [ ] **Step 8: Build for Android and verify on-device**

Run: `dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android -t:Run`
Expected: builds, deploys.

Manual check: open a sheet, tap the toolbar pen icon — panel opens showing Music Icons (tab highlighted). Tap the Pencil tab icon — content switches to the (currently mostly-empty) Pencil section, its tab highlights, panel stays open. Tap Eraser tab — same. Tap back to Music Icons — the category grid is still there and still works (place an icon, confirm no regression). With Pencil or Eraser active, confirm the toolbar shows a second **X** icon; tap it — active tool resets to Music Icons, panel state unaffected, X disappears. Confirm the panel does *not* auto-close when switching tabs. Check logcat for crashes throughout.

- [ ] **Step 9: Run the full Core test suite**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
Expected: all 65 tests pass (unchanged from Step 5 — this task's remaining work is Presentation-only).

- [ ] **Step 10: Commit**

```bash
git add src/PiccoloReader.Core/ViewModels/AnnotationTool.cs src/PiccoloReader.Core/ViewModels/SheetViewerViewModel.cs src/PiccoloReader/Views/SheetViewerPage.xaml src/PiccoloReader/Views/SheetViewerPage.xaml.cs tests/PiccoloReader.Core.Tests/ViewModels/SheetViewerViewModelTests.cs
git commit -m "feat: three-tab tool panel (Music Icons/Pencil/Eraser), toolbar X to deactivate"
```

---

### Task 4: Pencil — color/width selectors, DrawingView overlay, commit strokes

**Files:**
- Modify: `src/PiccoloReader.Core/ViewModels/SheetViewerViewModel.cs`
- Modify: `src/PiccoloReader/Views/SheetViewerPage.xaml`
- Modify: `src/PiccoloReader/Views/SheetViewerPage.xaml.cs`

**Interfaces:**
- Consumes: `AnnotationService.AddStrokeAsync` (Task 1), `SheetViewerViewModel.ActiveTool`/`PencilSection` (Task 3).
- Produces: nothing further consumed by later tasks — Eraser (Task 5) is independent of Pencil's internals.

**Verified API** (via the actual `CommunityToolkit.Maui` 13.0.0 source, not assumed): `DrawingView.LineColor` is `Color`, `LineWidth` is `float` (both device-independent pixels, *not* normalized), `Lines` is `ObservableCollection<IDrawingLine>`, `ShouldClearOnFinish` **defaults to `false`** (must be set explicitly), `BackgroundColor` **defaults to light gray** (must be set to transparent), and `DrawingLineCompleted` fires `EventHandler<DrawingLineCompletedEventArgs>` whose `LastDrawingLine.Points` is `ObservableCollection<PointF>` in the control's own local coordinate space.

- [ ] **Step 1: Add ViewModel state for remembered pencil color/width**

Add to `SheetViewerViewModel`, near the other `[ObservableProperty]` fields:

```csharp
[ObservableProperty]
private string _pencilColorHex = "#000000";

[ObservableProperty]
private double _pencilStrokeWidth = 0.008;
```

(`0.008` normalized ≈ a comfortable middle-of-range pencil thickness relative to page width — a Presentation-layer detail, not architecturally significant per the spec.)

- [ ] **Step 2: Add xmlns and DrawingView overlay to SheetViewerPage.xaml**

Add to the `<ContentPage>` root tag's namespace declarations:

```xml
xmlns:ctViews="clr-namespace:CommunityToolkit.Maui.Views;assembly=CommunityToolkit.Maui"
```

Inside `PageContainer`, add the `DrawingView` as a new child, after `SelectionOverlay`'s closing `</Grid>` (so it renders on top, same z-order reasoning as the existing overlays):

```xml
<ctViews:DrawingView
    x:Name="PencilDrawingView"
    IsVisible="False"
    InputTransparent="True"
    BackgroundColor="Transparent"
    ShouldClearOnFinish="True"
    DrawingLineCompleted="OnPencilDrawingLineCompleted" />
```

- [ ] **Step 3: Fill in the Pencil section's color/width controls**

Replace the placeholder `PencilSection` content from Task 3 with:

```xml
<VerticalStackLayout x:Name="PencilSection" IsVisible="False" Spacing="8">
    <Label Text="Pencil" FontAttributes="Bold" FontSize="16" />
    <Label Text="Color" FontSize="14" />
    <HorizontalStackLayout Spacing="8">
        <Border WidthRequest="32" HeightRequest="32" BackgroundColor="Black" StrokeThickness="1">
            <Border.GestureRecognizers>
                <TapGestureRecognizer Tapped="OnPencilColorTapped" CommandParameter="#000000" />
            </Border.GestureRecognizers>
        </Border>
        <Border WidthRequest="32" HeightRequest="32" BackgroundColor="Red" StrokeThickness="1">
            <Border.GestureRecognizers>
                <TapGestureRecognizer Tapped="OnPencilColorTapped" CommandParameter="#FF0000" />
            </Border.GestureRecognizers>
        </Border>
        <Border WidthRequest="32" HeightRequest="32" BackgroundColor="Blue" StrokeThickness="1">
            <Border.GestureRecognizers>
                <TapGestureRecognizer Tapped="OnPencilColorTapped" CommandParameter="#0000FF" />
            </Border.GestureRecognizers>
        </Border>
        <Border WidthRequest="32" HeightRequest="32" BackgroundColor="Green" StrokeThickness="1">
            <Border.GestureRecognizers>
                <TapGestureRecognizer Tapped="OnPencilColorTapped" CommandParameter="#008000" />
            </Border.GestureRecognizers>
        </Border>
        <Border WidthRequest="32" HeightRequest="32" BackgroundColor="Orange" StrokeThickness="1">
            <Border.GestureRecognizers>
                <TapGestureRecognizer Tapped="OnPencilColorTapped" CommandParameter="#FFA500" />
            </Border.GestureRecognizers>
        </Border>
    </HorizontalStackLayout>
    <Label Text="Width" FontSize="14" />
    <Slider x:Name="PencilWidthSlider" Minimum="0.003" Maximum="0.02" Value="{Binding PencilStrokeWidth}" ValueChanged="OnPencilWidthChanged" />
</VerticalStackLayout>
```

- [ ] **Step 4: Wire up color/width handlers and DrawingView activation in SheetViewerPage.xaml.cs**

Add `using CommunityToolkit.Maui.Views;` and `using Microsoft.Maui.Graphics;` to the top of the file if not already present (`Microsoft.Maui.Graphics` types like `Color`/`PointF` are implicitly available in MAUI XAML code-behind already via global usings, but add explicitly if the build complains).

Add methods:

```csharp
private void OnPencilColorTapped(object? sender, TappedEventArgs e)
{
    if (e.Parameter is string colorHex)
    {
        _viewModel.PencilColorHex = colorHex;
        PencilDrawingView.LineColor = Color.FromArgb(colorHex);
    }
}

private void OnPencilWidthChanged(object? sender, ValueChangedEventArgs e)
{
    UpdatePencilDrawingViewLineWidth();
}

private void UpdatePencilDrawingViewLineWidth()
{
    if (PageContainer.Width > 0)
    {
        PencilDrawingView.LineWidth = (float)(_viewModel.PencilStrokeWidth * PageContainer.Width);
    }
}

private async void OnPencilDrawingLineCompleted(object? sender, DrawingLineCompletedEventArgs e)
{
    if (_viewModel.SheetId is null || PageContainer.Width <= 0 || PageContainer.Height <= 0)
    {
        return;
    }

    var linePoints = e.LastDrawingLine.Points;
    if (linePoints.Count < 2)
    {
        return;
    }

    var normalizedPoints = linePoints
        .Select(p => new StrokePoint(p.X / PageContainer.Width, p.Y / PageContainer.Height))
        .ToList();

    if (!int.TryParse(SheetId, out var sheetId))
    {
        return;
    }

    var annotation = await _viewModel.AddStrokeAsync(sheetId, _viewModel.PencilColorHex, _viewModel.PencilStrokeWidth, normalizedPoints);
    AnnotationCanvas.InvalidateSurface();
}
```

(`_viewModel.SheetId is null` in the guard above is checking the wrong thing — `SheetViewerViewModel` doesn't expose a `SheetId`; use the page's own `SheetId` string property parsed the same way `OnAppearing` already does. Remove that first condition from the guard, keep the `PageContainer.Width/Height` and `int.TryParse` checks, matching the pattern below.)

Corrected version of the guard (use this, not the draft above):

```csharp
private async void OnPencilDrawingLineCompleted(object? sender, DrawingLineCompletedEventArgs e)
{
    if (PageContainer.Width <= 0 || PageContainer.Height <= 0 || !int.TryParse(SheetId, out var sheetId))
    {
        return;
    }

    var linePoints = e.LastDrawingLine.Points;
    if (linePoints.Count < 2)
    {
        return;
    }

    var normalizedPoints = linePoints
        .Select(p => new StrokePoint(p.X / PageContainer.Width, p.Y / PageContainer.Height))
        .ToList();

    await _viewModel.AddStrokeAsync(sheetId, _viewModel.PencilColorHex, _viewModel.PencilStrokeWidth, normalizedPoints);
    AnnotationCanvas.InvalidateSurface();
}
```

Add `using PiccoloReader.Core.Data.Models;` to the top of the file if not already present (it already is, from `Annotation`).

Update `UpdateToolSections()` (from Task 3) to also toggle the `DrawingView`'s visibility/interactivity and refresh its line width when Pencil becomes active:

```csharp
private void UpdateToolSections()
{
    MusicIconsSection.IsVisible = _viewModel.ActiveTool == AnnotationTool.MusicIcons;
    PencilSection.IsVisible = _viewModel.ActiveTool == AnnotationTool.Pencil;
    EraserSection.IsVisible = _viewModel.ActiveTool == AnnotationTool.Eraser;

    MusicIconsTabButton.BackgroundColor = _viewModel.ActiveTool == AnnotationTool.MusicIcons ? TabActiveColor : TabInactiveColor;
    PencilTabButton.BackgroundColor = _viewModel.ActiveTool == AnnotationTool.Pencil ? TabActiveColor : TabInactiveColor;
    EraserTabButton.BackgroundColor = _viewModel.ActiveTool == AnnotationTool.Eraser ? TabActiveColor : TabInactiveColor;

    var pencilActive = _viewModel.ActiveTool == AnnotationTool.Pencil;
    PencilDrawingView.IsVisible = pencilActive;
    PencilDrawingView.InputTransparent = !pencilActive;
    if (pencilActive)
    {
        PencilDrawingView.LineColor = Color.FromArgb(_viewModel.PencilColorHex);
        UpdatePencilDrawingViewLineWidth();
    }
}
```

- [ ] **Step 5: Add SheetViewerViewModel.AddStrokeAsync**

Add to `SheetViewerViewModel`, near `PlaceIconAsync`:

```csharp
public async Task<Annotation> AddStrokeAsync(int sheetId, string colorHex, double strokeWidth, IReadOnlyList<StrokePoint> points)
{
    var annotation = await _annotationService.AddStrokeAsync(sheetId, CurrentPageIndex, colorHex, strokeWidth, points);
    CurrentPageAnnotations.Add(annotation);
    return annotation;
}
```

- [ ] **Step 6: Draw committed strokes in OnAnnotationCanvasPaintSurface**

Modify `OnAnnotationCanvasPaintSurface` in `SheetViewerPage.xaml.cs` — the existing loop only handles icons via `MusicIconCatalog.FindByKey`. Add a branch for strokes:

```csharp
private void OnAnnotationCanvasPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
{
    var canvas = e.Surface.Canvas;
    canvas.Clear(SKColors.Transparent);

    var info = e.Info;

    foreach (var annotation in _viewModel.CurrentPageAnnotations)
    {
        if (annotation.IsStroke)
        {
            DrawStroke(canvas, annotation, info);
            continue;
        }

        var icon = MusicIconCatalog.FindByKey(annotation.IconKey);
        if (icon is null)
        {
            continue;
        }

        var targetRect = new SKRect(
            (float)(annotation.X * info.Width),
            (float)(annotation.Y * info.Height),
            (float)((annotation.X + annotation.Width) * info.Width),
            (float)((annotation.Y + annotation.Height) * info.Height));

        DrawGlyphFitted(canvas, icon.Codepoint, targetRect);
    }
}

private void DrawStroke(SKCanvas canvas, Annotation annotation, SKImageInfo info)
{
    var points = AnnotationService.DeserializePoints(annotation.Points);
    if (points.Count < 2 || annotation.ColorHex is null)
    {
        return;
    }

    using var path = new SKPath();
    path.MoveTo((float)(points[0].X * info.Width), (float)(points[0].Y * info.Height));
    for (var i = 1; i < points.Count; i++)
    {
        path.LineTo((float)(points[i].X * info.Width), (float)(points[i].Y * info.Height));
    }

    using var paint = new SKPaint
    {
        Color = SKColor.Parse(annotation.ColorHex),
        StrokeWidth = (float)(annotation.StrokeWidth * info.Width),
        Style = SKPaintStyle.Stroke,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round,
        IsAntialias = true
    };

    canvas.DrawPath(path, paint);
}
```

Add `using PiccoloReader.Core.Services;` to the top of the file if not already present (it already is, from `MusicIconCatalog`).

- [ ] **Step 7: Build for Android and verify on-device**

Run: `dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android -t:Run`
Expected: builds, deploys.

Manual check: open a sheet, open the tool panel, tap Pencil tab — color swatches and width slider appear, panel stays open. Tap a color (e.g. Red), drag the width slider. Close the panel (toolbar toggle) — confirm the page is now in "drawing mode" (the X icon shows in the toolbar). Drag a finger across the page — ink should appear live, in the chosen color, at a reasonable thickness. Lift — the stroke should persist and remain visible (drawn by `AnnotationCanvas` now, not `DrawingView`, but visually seamless). Draw a second, separate stroke — confirm it works independently (proving `ShouldClearOnFinish` correctly reset the control). Turn to another page and back — confirm the stroke persisted and reloads correctly (reusing the same per-page annotation reload already proven for icons). Tap the toolbar X — confirm drawing mode exits, dragging the page now pans/turns pages again as normal. Check logcat for crashes throughout.

- [ ] **Step 8: Run the full Core test suite**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
Expected: all 65 tests pass (unchanged — this task is Presentation-only plus one small ViewModel method with no new test needed beyond what Task 1 already covers for the underlying service call).

- [ ] **Step 9: Commit**

```bash
git add src/PiccoloReader.Core/ViewModels/SheetViewerViewModel.cs src/PiccoloReader/Views/SheetViewerPage.xaml src/PiccoloReader/Views/SheetViewerPage.xaml.cs
git commit -m "feat: Pencil tool - color/width selectors, DrawingView overlay, commit strokes"
```

---

### Task 5: Eraser — size selector, gesture hit-testing, radius circle, delete

**Files:**
- Modify: `src/PiccoloReader.Core/ViewModels/SheetViewerViewModel.cs`
- Modify: `src/PiccoloReader/Views/SheetViewerPage.xaml`
- Modify: `src/PiccoloReader/Views/SheetViewerPage.xaml.cs`

**Interfaces:**
- Consumes: `StrokeHitTester.DistanceToPolyline` (Task 2), `SheetViewerViewModel.ActiveTool` (Task 3), `AnnotationService.DeserializePoints` (Task 1).
- Produces: nothing further — last task in this plan.

- [ ] **Step 1: Add ViewModel state for remembered eraser radius**

Add to `SheetViewerViewModel`, near `PencilStrokeWidth`:

```csharp
[ObservableProperty]
private double _eraserRadius = 0.05;
```

- [ ] **Step 2: Fill in the Eraser section's size slider**

Replace the placeholder `EraserSection` content from Task 3 with:

```xml
<VerticalStackLayout x:Name="EraserSection" IsVisible="False" Spacing="8">
    <Label Text="Eraser" FontAttributes="Bold" FontSize="16" />
    <Label Text="Size" FontSize="14" />
    <Slider Minimum="0.02" Maximum="0.08" Value="{Binding EraserRadius}" />
</VerticalStackLayout>
```

- [ ] **Step 3: Add the eraser radius-circle indicator to SheetViewerPage.xaml**

Inside `PageContainer`, add after `PencilDrawingView`:

```xml
<Ellipse
    x:Name="EraserRadiusIndicator"
    IsVisible="False"
    InputTransparent="True"
    Fill="{AppThemeBinding Light={StaticResource Primary}, Dark={StaticResource PrimaryDark}}"
    Opacity="0.3"
    HorizontalOptions="Start"
    VerticalOptions="Start" />
```

- [ ] **Step 4: Add a PointerGestureRecognizer to PageContainer for eraser drag tracking**

`PanGestureRecognizer` only exposes cumulative deltas from gesture start (the same limitation the Pencil task's spec revision already documented for `PanGestureRecognizer` generally), so it can't report where a drag currently is on the page in absolute terms — only where it's moved *relative to* an unknown start point. `TapGestureRecognizer`'s `OnPageContainerTapped` (existing code) already solves this for taps via `e.GetPosition(PageContainer)`, which returns the touch position in `PageContainer`'s local content coordinates, already correct at any zoom level (proven by the existing icon tap-to-select feature). `PointerGestureRecognizer` (available in this project's `net10.0-android` target) exposes that same `GetPosition` API continuously, via `PointerPressed`/`PointerMoved`/`PointerReleased`, which is exactly what continuous eraser hit-testing needs — so use it instead of extending `OnPanUpdated`.

Add to `PageContainer.GestureRecognizers` in `SheetViewerPage.xaml`, alongside the existing three:

```xml
<PointerGestureRecognizer
    PointerPressed="OnPageContainerPointerPressed"
    PointerMoved="OnPageContainerPointerMoved"
    PointerReleased="OnPageContainerPointerReleased" />
```

Modify `OnPanUpdated` in `SheetViewerPage.xaml.cs` to bail out while Eraser is active, so `PanGestureRecognizer` doesn't fight `PointerGestureRecognizer` for the same drag (page-turn/zoom-pan must not also trigger). Insert the new early-return right after the existing `ToolPanel.IsVisible` check, before the existing zoom-pan/swipe switch (which is otherwise unchanged):

```csharp
private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
{
    if (ToolPanel.IsVisible)
    {
        if (e.StatusType == GestureStatus.Completed)
        {
            ToolPanel.IsVisible = false;
        }

        return;
    }

    if (_viewModel.ActiveTool == AnnotationTool.Eraser)
    {
        return;
    }

    switch (e.StatusType)
    {
        // ... existing zoom-pan/swipe-turn-page cases, unchanged
    }
}
```

Add the three pointer handlers and their shared helper, anywhere in the class body:

```csharp
private void OnPageContainerPointerPressed(object? sender, PointerEventArgs e)
{
    if (_viewModel.ActiveTool != AnnotationTool.Eraser)
    {
        return;
    }

    ShowEraserRadiusIndicator();
    HandleEraserPointerEvent(e);
}

private void OnPageContainerPointerMoved(object? sender, PointerEventArgs e)
{
    if (_viewModel.ActiveTool == AnnotationTool.Eraser)
    {
        HandleEraserPointerEvent(e);
    }
}

private void OnPageContainerPointerReleased(object? sender, PointerEventArgs e)
{
    if (_viewModel.ActiveTool == AnnotationTool.Eraser)
    {
        EraserRadiusIndicator.IsVisible = false;
    }
}

private void HandleEraserPointerEvent(PointerEventArgs e)
{
    if (PageContainer.Width <= 0 || PageContainer.Height <= 0)
    {
        return;
    }

    var position = e.GetPosition(PageContainer);
    if (position is not { } point)
    {
        return;
    }

    var normalizedX = point.X / PageContainer.Width;
    var normalizedY = point.Y / PageContainer.Height;
    UpdateEraserRadiusIndicator(normalizedX, normalizedY);
    EraseAt(normalizedX, normalizedY);
}

private void ShowEraserRadiusIndicator()
{
    var radiusPx = _viewModel.EraserRadius * PageContainer.Width;
    EraserRadiusIndicator.WidthRequest = radiusPx * 2;
    EraserRadiusIndicator.HeightRequest = radiusPx * 2;
    EraserRadiusIndicator.IsVisible = true;
}

private void UpdateEraserRadiusIndicator(double normalizedX, double normalizedY)
{
    var radiusPx = _viewModel.EraserRadius * PageContainer.Width;
    EraserRadiusIndicator.TranslationX = normalizedX * PageContainer.Width - radiusPx;
    EraserRadiusIndicator.TranslationY = normalizedY * PageContainer.Height - radiusPx;
}

private void EraseAt(double normalizedX, double normalizedY)
{
    var radius = _viewModel.EraserRadius;
    var hit = _viewModel.CurrentPageAnnotations.FirstOrDefault(a =>
    {
        if (a.IsStroke)
        {
            var points = AnnotationService.DeserializePoints(a.Points);
            return StrokeHitTester.DistanceToPolyline(normalizedX, normalizedY, points) <= radius;
        }

        return normalizedX >= a.X - radius && normalizedX <= a.X + a.Width + radius &&
               normalizedY >= a.Y - radius && normalizedY <= a.Y + a.Height + radius;
    });

    if (hit is not null)
    {
        _ = _viewModel.EraseAnnotationAsync(hit);
        AnnotationCanvas.InvalidateSurface();
    }
}
```

- [ ] **Step 5: Add SheetViewerViewModel.EraseAnnotationAsync**

Add to `SheetViewerViewModel`, near `DeleteSelectedAnnotationAsync`:

```csharp
public async Task EraseAnnotationAsync(Annotation annotation)
{
    await _annotationService.DeleteAnnotationAsync(annotation);
    CurrentPageAnnotations.Remove(annotation);
    if (SelectedAnnotation == annotation)
    {
        SelectedAnnotation = null;
    }
}
```

- [ ] **Step 6: Build for Android and verify on-device**

Run: `dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android -t:Run`
Expected: builds, deploys.

Manual check: open a sheet with a placed icon, open the tool panel, tap Eraser tab, adjust the size slider, close the panel. Touch down on the page and confirm the radius-circle indicator appears immediately under the finger (proving `PointerGestureRecognizer`'s `GetPosition` reports the correct absolute position on first touch, not just after movement — this is the one thing that couldn't be confirmed on paper, since it depends on whether Android's touch-to-pointer-event mapping in this MAUI version fires `PointerPressed` at the actual touch-down coordinates). Drag across a placed icon — confirm it deletes on contact. Drag across a placed pencil stroke (from Task 4) — confirm it deletes on contact. Drag over empty space — confirm nothing happens. Confirm `PageContainer`'s pinch/pan/tap behavior is unaffected when Eraser is *not* active (proving the new `PointerGestureRecognizer` doesn't interfere with the existing `Tap`/`Pinch`/`Pan` recognizers already on the same element, consistent with the established same-element/different-type stacking pattern from PR #36). Check logcat for crashes throughout.

If `PointerPressed`/`PointerMoved` turn out not to fire reliably for touch on this Android target (unlikely, but unverifiable without a device), fall back to reinstating a `PanGestureRecognizer`-based approach seeded from the *nearest currently-rendered annotation* under a rough screen-center estimate — but attempt the `PointerGestureRecognizer` approach above first, since it is more precise and simpler when it works.

- [ ] **Step 7: Run the full Core test suite**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
Expected: all 65 tests pass (unchanged — this task is Presentation-only plus one small ViewModel method already exercised indirectly by existing delete-annotation coverage).

- [ ] **Step 8: Commit**

```bash
git add src/PiccoloReader.Core/ViewModels/SheetViewerViewModel.cs src/PiccoloReader/Views/SheetViewerPage.xaml src/PiccoloReader/Views/SheetViewerPage.xaml.cs
git commit -m "feat: Eraser tool - size selector, drag-to-erase icons and strokes, radius indicator"
```

- [ ] **Step 9: Update the roadmap**

In `docs/superpowers/plans/roadmap.md`, add a "Plan 4: Pencil Annotations + Eraser" task table (same shape as Plan 3's), filling in PR links as each task's PR is opened/merged during execution.

---

## Post-Plan Notes

- **Eraser touch tracking (Task 5)** uses `PointerGestureRecognizer` rather than extending `OnPanUpdated`, since `PanGestureRecognizer` only exposes cumulative deltas from an unknown start point, not absolute position — the same limitation the Pencil task's spec revision documented for `PanGestureRecognizer` generally. `PointerGestureRecognizer.GetPosition` uses the same coordinate convention already proven correct at any zoom level by the existing tap-to-select feature. The one thing this plan can't confirm on paper is whether `PointerPressed` fires reliably for touch (not just mouse/hover) on this Android target — Task 5 Step 6 verifies this on-device and documents a fallback if not.
- **Undo/redo (Plan 5)** is not in this plan, per the spec. Every stroke add and eraser delete commits immediately.
- **Stroke selection/move/resize** is not in this plan, per the spec's explicit Out of Scope section — only the Eraser removes a stroke, and only whole.
