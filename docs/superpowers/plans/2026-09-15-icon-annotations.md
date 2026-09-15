# Icon Annotations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user open the Sheet Viewer's tool panel, pick a music symbol (dynamics, articulations, fermata/breath, hairpins) from a categorized picker, place it on the page, then move/resize/delete it — persisted per sheet per page, reloaded automatically when the sheet is reopened.

**Architecture:** A new `Annotation` table (Core) stores icon stamps per sheet/page in normalized (0.0–1.0) coordinates, matching the existing architecture spec's data model. `AnnotationService` (Core) does CRUD, mirroring `LibraryService`'s pattern exactly. `SheetViewerViewModel` gains a per-page `CurrentPageAnnotations` collection and a selection concept. On the Presentation side, a right-side sliding tool panel (pen-icon toolbar button to open/close) hosts a "Music Icons" picker (categorized, tap to place at a default centered size); placed icons render on an `SKCanvasView` overlay stacked on top of the existing PDF page `Image` (both wrapped in one container so the existing pinch/pan/tap-to-turn transform — already built in Plan 2 — applies to annotations too); a small selection overlay (border, resize handle, delete button) appears over the tapped icon for move/resize/delete.

**Tech Stack:** .NET MAUI (Android only, same scope as Plan 2), CommunityToolkit.Mvvm, sqlite-net-pcl, SkiaSharp (`SkiaSharp.Views.Maui.Controls` — already available transitively via `SkiaSharp.Extended.UI.Maui`, no new package), the Bravura SMuFL music font (SIL OFL license) for real music symbol glyphs.

**Spec:** [`docs/superpowers/specs/2026-09-14-architecture-design.md`](../specs/2026-09-14-architecture-design.md) — "Data Model" (the `Annotation` table) and "Annotation Editing & Undo/Redo" (the tool panel, Music Icons tab, move/resize) sections. This plan implements the icon-stamp half of that design; pencil strokes and the Eraser tool are Plan 4, per the roadmap's split.

## Icon Set (resolved — was an open question in `requirements.md`)

Confirmed with the user: **Dynamics, Articulations, Fermata & Breath, Hairpins** (Clefs excluded — already in the printed score, not commonly hand-annotated). Glyphs come from **Bravura** (`steinbergmedia/bravura`, SIL OFL-1.1), the SMuFL reference font — verified for real, before writing this plan: downloaded `redist/otf/Bravura.otf`, looked up each glyph's codepoint in the official SMuFL `glyphnames.json` registry (`steinbergmedia/smufl` releases), then confirmed via `fonttools` that every codepoint below resolves to a real, non-degenerate outline in the actual font file (not just present in the metadata).

`mp`/`mf` are deliberately **excluded from v1** — Bravura has no single pre-composed glyph for them (SMuFL builds them from two glyphs, `dynamicMezzo` + `dynamicPiano`/`dynamicForte`, side by side), which would require two-glyph composition logic everywhere (picker button, on-page rendering) instead of the simple "one codepoint = one icon" model every other symbol uses. Not worth the complexity for a first pass — flagged in Post-Plan Notes as a natural follow-up.

| Category | Icon | SMuFL name | Codepoint | AspectRatio (w/h, for default sizing) |
|---|---|---|---|---|
| Dynamics | pp | dynamicPP | U+E52B | 1.95 |
| Dynamics | p | dynamicPiano | U+E520 | 1.09 |
| Dynamics | f | dynamicForte | U+E522 | 0.85 |
| Dynamics | ff | dynamicFF | U+E52F | 1.25 |
| Dynamics | fp | dynamicFortePiano | U+E534 | 1.28 |
| Articulations | Staccato | articStaccatoAbove | U+E4A2 | 1.00 |
| Articulations | Accent | articAccentAbove | U+E4A0 | 1.39 |
| Articulations | Tenuto | articTenutoAbove | U+E4A4 | 7.06 |
| Articulations | Marcato | articMarcatoAbove | U+E4AC | 0.93 |
| Fermata & Breath | Fermata | fermataAbove | U+E4C0 | 1.81 |
| Fermata & Breath | Breath mark | breathMarkComma | U+E4CE | 0.61 |
| Hairpins | Crescendo | dynamicCrescendoHairpin | U+E53E | 2.78 |
| Hairpins | Decrescendo | dynamicDiminuendoHairpin | U+E53F | 2.78 |

## Global Constraints

- Android only, same as Plan 2 — no iOS-specific work in this plan.
- `Microsoft.Maui.Controls` version floor is 10.0.20 — no new package needed for this plan (SkiaSharp is already present).
- `IconKey` in the data model is the SMuFL glyph name (e.g. `"dynamicForte"`) — a string, not an enum, so the icon set can grow later without a schema migration (matches the architecture spec's explicit reasoning for this field).
- Normalized coordinates (0.0–1.0 relative to page width/height) for all annotation position/size fields — matches the existing convention and the architecture spec.
- Follow existing MVVM pattern exactly: `ObservableObject` + `[ObservableProperty]` + `[RelayCommand]`.
- One task = one PR. Wait for explicit merge confirmation before starting the next task's branch.
- Every PR that changes on-screen behavior needs on-device verification (build for Android, exercise the flow, check logcat for crashes) before opening the PR — same standard as Plan 2.

---

### Task 1: Bundle Bravura font + Annotation data model + MusicIconCatalog

**Files:**
- Create: `src/PiccoloReader/Resources/Fonts/Bravura.otf`
- Create: `src/PiccoloReader/Resources/Raw/Bravura.otf` (a second copy — Skia needs to load it as a raw byte stream via `FileSystem.OpenAppPackageFileAsync`, separate from the `Resources/Fonts` registration MAUI's XAML `FontFamily`/`FontImageSource` mechanism uses)
- Modify: `src/PiccoloReader/MauiProgram.cs` (register the font)
- Create: `src/PiccoloReader.Core/Data/Models/Annotation.cs`
- Modify: `src/PiccoloReader.Core/Data/AppDatabase.cs` (create the new table)
- Create: `src/PiccoloReader.Core/Services/MusicIconCatalog.cs`
- Test: `tests/PiccoloReader.Core.Tests/Services/MusicIconCatalogTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks (first task of this plan).
- Produces (consumed by later tasks):
  - `PiccoloReader.Core.Data.Models.Annotation` — `Id`, `SheetId` (int), `PageIndex` (int, 0-based, matching `SheetViewerViewModel.CurrentPageIndex`), `IconKey` (string), `X`/`Y`/`Width`/`Height` (double, normalized), `CreatedAt` (DateTime).
  - `PiccoloReader.Core.Services.MusicIcon` record — `Key`, `DisplayName`, `Codepoint` (int), `AspectRatio` (double).
  - `PiccoloReader.Core.Services.MusicIconCategory` record — `Name`, `Icons` (`IReadOnlyList<MusicIcon>`).
  - `MusicIconCatalog.Categories` (`IReadOnlyList<MusicIconCategory>`) and `MusicIconCatalog.FindByKey(string key)` (`MusicIcon?`).
  - `"Bravura"` registered as a font family, usable from XAML exactly like `"MaterialOutlined"` already is.

- [ ] **Step 1: Get the verified font file into the project**

The font was already downloaded and verified against the real SMuFL glyph registry while writing this plan (every codepoint in the Icon Set table above was confirmed to resolve to a real, non-degenerate outline via `fonttools`). Download it fresh (SIL OFL-1.1 licensed, from the official `steinbergmedia/bravura` repo) into both locations:

```bash
curl -sL -o src/PiccoloReader/Resources/Fonts/Bravura.otf "https://raw.githubusercontent.com/steinbergmedia/bravura/master/redist/otf/Bravura.otf"
cp src/PiccoloReader/Resources/Fonts/Bravura.otf src/PiccoloReader/Resources/Raw/Bravura.otf
```

- [ ] **Step 2: Register the font in MauiProgram.cs**

In `src/PiccoloReader/MauiProgram.cs`, add alongside the existing `fonts.AddFont(...)` calls:

```csharp
fonts.AddFont("Bravura.otf", "Bravura");
```

- [ ] **Step 3: Write the Annotation model**

```csharp
// src/PiccoloReader.Core/Data/Models/Annotation.cs
using SQLite;

namespace PiccoloReader.Core.Data.Models;

public class Annotation
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int SheetId { get; set; }

    public int PageIndex { get; set; }

    public string IconKey { get; set; } = string.Empty;

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public DateTime CreatedAt { get; set; }
}
```

- [ ] **Step 4: Register the table in AppDatabase**

In `src/PiccoloReader.Core/Data/AppDatabase.cs`:

```csharp
public async Task InitializeAsync()
{
    await Connection.CreateTableAsync<Folder>();
    await Connection.CreateTableAsync<Sheet>();
    await Connection.CreateTableAsync<Annotation>();
}
```

- [ ] **Step 5: Write the failing catalog test**

```csharp
// tests/PiccoloReader.Core.Tests/Services/MusicIconCatalogTests.cs
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests.Services;

public class MusicIconCatalogTests
{
    [Fact]
    public void Categories_HasFourCategoriesWithExpectedNames()
    {
        var names = MusicIconCatalog.Categories.Select(c => c.Name).ToList();

        Assert.Equal(new[] { "Dynamics", "Articulations", "Fermata & Breath", "Hairpins" }, names);
    }

    [Fact]
    public void Categories_AllIconsHaveUniqueKeys()
    {
        var keys = MusicIconCatalog.Categories.SelectMany(c => c.Icons).Select(i => i.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void FindByKey_KnownKey_ReturnsIcon()
    {
        var icon = MusicIconCatalog.FindByKey("dynamicForte");

        Assert.NotNull(icon);
        Assert.Equal("f", icon!.DisplayName);
        Assert.Equal(0xE522, icon.Codepoint);
    }

    [Fact]
    public void FindByKey_UnknownKey_ReturnsNull()
    {
        Assert.Null(MusicIconCatalog.FindByKey("notARealIcon"));
    }
}
```

- [ ] **Step 6: Run test to verify it fails**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter "FullyQualifiedName~MusicIconCatalogTests"`
Expected: FAIL to compile — `MusicIconCatalog` doesn't exist yet.

- [ ] **Step 7: Write the catalog**

```csharp
// src/PiccoloReader.Core/Services/MusicIconCatalog.cs
namespace PiccoloReader.Core.Services;

public record MusicIcon(string Key, string DisplayName, int Codepoint, double AspectRatio);

public record MusicIconCategory(string Name, IReadOnlyList<MusicIcon> Icons);

public static class MusicIconCatalog
{
    public static IReadOnlyList<MusicIconCategory> Categories { get; } = new List<MusicIconCategory>
    {
        new("Dynamics", new List<MusicIcon>
        {
            new("dynamicPP", "pp", 0xE52B, 1.95),
            new("dynamicPiano", "p", 0xE520, 1.09),
            new("dynamicForte", "f", 0xE522, 0.85),
            new("dynamicFF", "ff", 0xE52F, 1.25),
            new("dynamicFortePiano", "fp", 0xE534, 1.28),
        }),
        new("Articulations", new List<MusicIcon>
        {
            new("articStaccatoAbove", "Staccato", 0xE4A2, 1.00),
            new("articAccentAbove", "Accent", 0xE4A0, 1.39),
            new("articTenutoAbove", "Tenuto", 0xE4A4, 7.06),
            new("articMarcatoAbove", "Marcato", 0xE4AC, 0.93),
        }),
        new("Fermata & Breath", new List<MusicIcon>
        {
            new("fermataAbove", "Fermata", 0xE4C0, 1.81),
            new("breathMarkComma", "Breath mark", 0xE4CE, 0.61),
        }),
        new("Hairpins", new List<MusicIcon>
        {
            new("dynamicCrescendoHairpin", "Crescendo", 0xE53E, 2.78),
            new("dynamicDiminuendoHairpin", "Decrescendo", 0xE53F, 2.78),
        }),
    };

    public static MusicIcon? FindByKey(string key) =>
        Categories.SelectMany(c => c.Icons).FirstOrDefault(i => i.Key == key);
}
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
Expected: all tests pass (total count goes from 35 to 39).

- [ ] **Step 9: Build for Android to verify the font registers cleanly**

Run: `dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 10: Commit**

```bash
git add src/PiccoloReader/Resources/Fonts/Bravura.otf src/PiccoloReader/Resources/Raw/Bravura.otf src/PiccoloReader/MauiProgram.cs src/PiccoloReader.Core/Data/Models/Annotation.cs src/PiccoloReader.Core/Data/AppDatabase.cs src/PiccoloReader.Core/Services/MusicIconCatalog.cs tests/PiccoloReader.Core.Tests/Services/MusicIconCatalogTests.cs
git commit -m "feat: add Annotation model, MusicIconCatalog, bundle Bravura font"
```

---

### Task 2: AnnotationService

**Files:**
- Create: `src/PiccoloReader.Core/Services/AnnotationService.cs`
- Test: `tests/PiccoloReader.Core.Tests/Services/AnnotationServiceTests.cs`

**Interfaces:**
- Consumes: `Annotation` model, `AppDatabase` (Task 1).
- Produces (consumed by Task 3):
  - `AnnotationService(AppDatabase database)`
  - `Task<List<Annotation>> GetAnnotationsAsync(int sheetId, int pageIndex)`
  - `Task<Annotation> AddIconAsync(int sheetId, int pageIndex, string iconKey, double x, double y, double width, double height)`
  - `Task UpdateAnnotationAsync(Annotation annotation)`
  - `Task DeleteAnnotationAsync(Annotation annotation)`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/PiccoloReader.Core.Tests/Services/AnnotationServiceTests.cs
using PiccoloReader.Core.Data;
using PiccoloReader.Core.Services;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.Services;

public class AnnotationServiceTests : IDisposable
{
    private readonly TestAppStorageProvider _storage = new();
    private readonly AnnotationService _sut;

    public AnnotationServiceTests()
    {
        Batteries_V2.Init();
        var database = new AppDatabase(_storage.DatabasePath);
        database.InitializeAsync().GetAwaiter().GetResult();
        _sut = new AnnotationService(database);
    }

    public void Dispose() => _storage.Dispose();

    [Fact]
    public async Task AddIconAsync_InsertsRetrievableAnnotation()
    {
        var annotation = await _sut.AddIconAsync(sheetId: 1, pageIndex: 0, iconKey: "dynamicForte", x: 0.4, y: 0.5, width: 0.1, height: 0.09);

        var loaded = await _sut.GetAnnotationsAsync(sheetId: 1, pageIndex: 0);

        Assert.Single(loaded);
        Assert.Equal(annotation.Id, loaded[0].Id);
        Assert.Equal("dynamicForte", loaded[0].IconKey);
        Assert.Equal(0.4, loaded[0].X);
    }

    [Fact]
    public async Task GetAnnotationsAsync_FiltersByPageIndex()
    {
        await _sut.AddIconAsync(1, pageIndex: 0, "dynamicForte", 0.1, 0.1, 0.1, 0.1);
        await _sut.AddIconAsync(1, pageIndex: 1, "dynamicPiano", 0.2, 0.2, 0.1, 0.1);

        var page0 = await _sut.GetAnnotationsAsync(1, pageIndex: 0);
        var page1 = await _sut.GetAnnotationsAsync(1, pageIndex: 1);

        Assert.Single(page0);
        Assert.Single(page1);
        Assert.Equal("dynamicForte", page0[0].IconKey);
        Assert.Equal("dynamicPiano", page1[0].IconKey);
    }

    [Fact]
    public async Task GetAnnotationsAsync_FiltersBySheetId()
    {
        await _sut.AddIconAsync(sheetId: 1, pageIndex: 0, "dynamicForte", 0.1, 0.1, 0.1, 0.1);
        await _sut.AddIconAsync(sheetId: 2, pageIndex: 0, "dynamicPiano", 0.2, 0.2, 0.1, 0.1);

        var sheet1 = await _sut.GetAnnotationsAsync(sheetId: 1, pageIndex: 0);

        Assert.Single(sheet1);
        Assert.Equal("dynamicForte", sheet1[0].IconKey);
    }

    [Fact]
    public async Task UpdateAnnotationAsync_PersistsPositionChange()
    {
        var annotation = await _sut.AddIconAsync(1, 0, "dynamicForte", 0.1, 0.1, 0.1, 0.1);
        annotation.X = 0.6;
        annotation.Y = 0.7;

        await _sut.UpdateAnnotationAsync(annotation);

        var loaded = await _sut.GetAnnotationsAsync(1, 0);
        Assert.Equal(0.6, loaded[0].X);
        Assert.Equal(0.7, loaded[0].Y);
    }

    [Fact]
    public async Task DeleteAnnotationAsync_RemovesRow()
    {
        var annotation = await _sut.AddIconAsync(1, 0, "dynamicForte", 0.1, 0.1, 0.1, 0.1);

        await _sut.DeleteAnnotationAsync(annotation);

        var loaded = await _sut.GetAnnotationsAsync(1, 0);
        Assert.Empty(loaded);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter "FullyQualifiedName~AnnotationServiceTests"`
Expected: FAIL to compile — `AnnotationService` doesn't exist yet.

- [ ] **Step 3: Implement AnnotationService**

```csharp
// src/PiccoloReader.Core/Services/AnnotationService.cs
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

    public Task UpdateAnnotationAsync(Annotation annotation) =>
        _database.Connection.UpdateAsync(annotation);

    public Task DeleteAnnotationAsync(Annotation annotation) =>
        _database.Connection.DeleteAsync(annotation);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
Expected: all tests pass (total count goes from 39 to 44).

- [ ] **Step 5: Commit**

```bash
git add src/PiccoloReader.Core/Services/AnnotationService.cs tests/PiccoloReader.Core.Tests/Services/AnnotationServiceTests.cs
git commit -m "feat: add AnnotationService"
```

---

### Task 3: Wire annotations into SheetViewerViewModel

**Files:**
- Modify: `src/PiccoloReader.Core/ViewModels/SheetViewerViewModel.cs`
- Modify: `src/PiccoloReader/MauiProgram.cs` (DI registration)
- Test: `tests/PiccoloReader.Core.Tests/ViewModels/SheetViewerViewModelTests.cs`

**Interfaces:**
- Consumes: `AnnotationService`, `MusicIconCatalog` (Task 1/2).
- Produces (consumed by Task 4-6):
  - `SheetViewerViewModel(LibraryService, IAppStorageProvider, IPdfPageRenderer, AnnotationService)` — new 4th constructor parameter.
  - `ObservableCollection<Annotation> CurrentPageAnnotations { get; }`
  - `Annotation? SelectedAnnotation { get; set; }` (settable from code-behind on tap-to-select)
  - `IAsyncRelayCommand<string> PlaceIconCommand` (takes an `iconKey`, generated from `PlaceIconAsync(string iconKey)`)
  - `Task MoveSelectedAnnotationAsync(double newX, double newY)`
  - `Task ResizeSelectedAnnotationAsync(double newWidth, double newHeight)`
  - `IAsyncRelayCommand DeleteSelectedAnnotationCommand`

- [ ] **Step 1: Write the failing tests**

Add to `tests/PiccoloReader.Core.Tests/ViewModels/SheetViewerViewModelTests.cs`. The constructor and every existing `new SheetViewerViewModel(...)` call needs the new `AnnotationService` parameter — add it to the test class's setup:

```csharp
// add near the top of the test class, alongside the other fields:
private readonly AnnotationService _annotationService;
```

```csharp
// in the constructor, after _libraryService is created:
_annotationService = new AnnotationService(_database);
_sut = new SheetViewerViewModel(_libraryService, _storage, _renderer, _annotationService);
```

(This replaces the existing `_sut = new SheetViewerViewModel(_libraryService, _storage, _renderer);` line — the constructor signature is changing.)

New tests, appended at the end of the class before the closing brace:

```csharp
[Fact]
public async Task LoadAsync_LoadsAnnotationsForCurrentPage()
{
    var sheet = await InsertSheetAsync(pageCount: 3);
    await _annotationService.AddIconAsync(sheet.Id, pageIndex: 0, "dynamicForte", 0.4, 0.5, 0.1, 0.09);

    await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

    Assert.Single(_sut.CurrentPageAnnotations);
    Assert.Equal("dynamicForte", _sut.CurrentPageAnnotations[0].IconKey);
}

[Fact]
public async Task NextPageAsync_ReloadsAnnotationsForNewPage()
{
    var sheet = await InsertSheetAsync(pageCount: 3);
    await _annotationService.AddIconAsync(sheet.Id, pageIndex: 1, "dynamicPiano", 0.3, 0.3, 0.1, 0.1);
    await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);
    Assert.Empty(_sut.CurrentPageAnnotations);

    await _sut.NextPageCommand.ExecuteAsync(null);

    Assert.Single(_sut.CurrentPageAnnotations);
    Assert.Equal("dynamicPiano", _sut.CurrentPageAnnotations[0].IconKey);
}

[Fact]
public async Task PlaceIconAsync_AddsAnnotationCenteredWithCatalogAspectRatio()
{
    var sheet = await InsertSheetAsync(pageCount: 3);
    await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

    await _sut.PlaceIconCommand.ExecuteAsync("dynamicForte");

    Assert.Single(_sut.CurrentPageAnnotations);
    var placed = _sut.CurrentPageAnnotations[0];
    Assert.Equal("dynamicForte", placed.IconKey);
    Assert.Equal(0.12, placed.Width, precision: 6);
    Assert.Equal(0.12 / 0.85, placed.Height, precision: 6);
    Assert.Equal(0.5 - placed.Width / 2, placed.X, precision: 6);
    Assert.Equal(0.5 - placed.Height / 2, placed.Y, precision: 6);

    var persisted = await _annotationService.GetAnnotationsAsync(sheet.Id, 0);
    Assert.Single(persisted);
}

[Fact]
public async Task PlaceIconAsync_SelectsTheNewIcon()
{
    var sheet = await InsertSheetAsync(pageCount: 3);
    await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

    await _sut.PlaceIconCommand.ExecuteAsync("dynamicForte");

    Assert.NotNull(_sut.SelectedAnnotation);
    Assert.Equal("dynamicForte", _sut.SelectedAnnotation!.IconKey);
}

[Fact]
public async Task MoveSelectedAnnotationAsync_UpdatesAndPersistsPosition()
{
    var sheet = await InsertSheetAsync(pageCount: 3);
    await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);
    await _sut.PlaceIconCommand.ExecuteAsync("dynamicForte");

    await _sut.MoveSelectedAnnotationAsync(0.2, 0.3);

    Assert.Equal(0.2, _sut.SelectedAnnotation!.X);
    Assert.Equal(0.3, _sut.SelectedAnnotation!.Y);
    var persisted = await _annotationService.GetAnnotationsAsync(sheet.Id, 0);
    Assert.Equal(0.2, persisted[0].X);
}

[Fact]
public async Task ResizeSelectedAnnotationAsync_UpdatesAndPersistsSize()
{
    var sheet = await InsertSheetAsync(pageCount: 3);
    await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);
    await _sut.PlaceIconCommand.ExecuteAsync("dynamicForte");

    await _sut.ResizeSelectedAnnotationAsync(0.2, 0.18);

    Assert.Equal(0.2, _sut.SelectedAnnotation!.Width);
    Assert.Equal(0.18, _sut.SelectedAnnotation!.Height);
    var persisted = await _annotationService.GetAnnotationsAsync(sheet.Id, 0);
    Assert.Equal(0.2, persisted[0].Width);
}

[Fact]
public async Task DeleteSelectedAnnotationAsync_RemovesFromCollectionAndPersistence()
{
    var sheet = await InsertSheetAsync(pageCount: 3);
    await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);
    await _sut.PlaceIconCommand.ExecuteAsync("dynamicForte");

    await _sut.DeleteSelectedAnnotationCommand.ExecuteAsync(null);

    Assert.Empty(_sut.CurrentPageAnnotations);
    Assert.Null(_sut.SelectedAnnotation);
    var persisted = await _annotationService.GetAnnotationsAsync(sheet.Id, 0);
    Assert.Empty(persisted);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter "FullyQualifiedName~SheetViewerViewModelTests"`
Expected: FAIL to compile — new constructor parameter and members don't exist yet.

- [ ] **Step 3: Update SheetViewerViewModel**

Full new content of `src/PiccoloReader.Core/ViewModels/SheetViewerViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.ViewModels;

public partial class SheetViewerViewModel : ObservableObject
{
    private const double DefaultIconWidth = 0.12;

    private readonly LibraryService _libraryService;
    private readonly IAppStorageProvider _storageProvider;
    private readonly IPdfPageRenderer _pdfPageRenderer;
    private readonly AnnotationService _annotationService;

    private Sheet? _sheet;
    private string _filePath = string.Empty;
    private int _targetWidthPx;
    private int _targetHeightPx;

    public SheetViewerViewModel(LibraryService libraryService, IAppStorageProvider storageProvider, IPdfPageRenderer pdfPageRenderer, AnnotationService annotationService)
    {
        _libraryService = libraryService;
        _storageProvider = storageProvider;
        _pdfPageRenderer = pdfPageRenderer;
        _annotationService = annotationService;
    }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyPropertyChangedFor(nameof(PageIndicatorText))]
    private int _pageCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyPropertyChangedFor(nameof(CurrentPageDisplay))]
    [NotifyPropertyChangedFor(nameof(PageIndicatorText))]
    private int _currentPageIndex;

    [ObservableProperty]
    private byte[]? _currentPageImageBytes;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedAnnotationCommand))]
    private Annotation? _selectedAnnotation;

    public ObservableCollection<Annotation> CurrentPageAnnotations { get; } = new();

    public int CurrentPageDisplay => CurrentPageIndex + 1;

    public string PageIndicatorText => $"Page {CurrentPageDisplay} of {PageCount}";

    public async Task LoadAsync(int sheetId, int targetWidthPx, int targetHeightPx)
    {
        IsLoading = true;
        try
        {
            _targetWidthPx = targetWidthPx;
            _targetHeightPx = targetHeightPx;

            var sheet = await _libraryService.GetSheetAsync(sheetId);
            _sheet = sheet;
            Title = sheet.Title;
            _filePath = Path.Combine(_storageProvider.SheetsDirectory, sheet.FileName);

            var pageCount = sheet.PageCount;
            if (pageCount <= 0)
            {
                pageCount = await _pdfPageRenderer.GetPageCountAsync(_filePath);
                await _libraryService.UpdateSheetPageCountAsync(sheet, pageCount);
            }

            PageCount = pageCount;
            CurrentPageIndex = pageCount > 0
                ? Math.Clamp(sheet.LastViewedPageIndex, 0, pageCount - 1)
                : 0;

            await LoadCurrentPageAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanGoToNextPage() => CurrentPageIndex < PageCount - 1;

    private bool CanGoToPreviousPage() => CurrentPageIndex > 0;

    private bool CanDeleteSelectedAnnotation() => SelectedAnnotation is not null;

    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    private async Task NextPageAsync()
    {
        CurrentPageIndex++;
        await LoadCurrentPageAsync();
        await PersistLastViewedPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPage))]
    private async Task PreviousPageAsync()
    {
        CurrentPageIndex--;
        await LoadCurrentPageAsync();
        await PersistLastViewedPageAsync();
    }

    [RelayCommand]
    private async Task PlaceIconAsync(string iconKey)
    {
        if (_sheet is null)
        {
            return;
        }

        var icon = MusicIconCatalog.FindByKey(iconKey);
        var aspectRatio = icon?.AspectRatio ?? 1.0;

        var width = DefaultIconWidth;
        var height = width / aspectRatio;
        var x = 0.5 - width / 2;
        var y = 0.5 - height / 2;

        var annotation = await _annotationService.AddIconAsync(_sheet.Id, CurrentPageIndex, iconKey, x, y, width, height);

        CurrentPageAnnotations.Add(annotation);
        SelectedAnnotation = annotation;
    }

    public async Task MoveSelectedAnnotationAsync(double newX, double newY)
    {
        if (SelectedAnnotation is null)
        {
            return;
        }

        SelectedAnnotation.X = newX;
        SelectedAnnotation.Y = newY;
        await _annotationService.UpdateAnnotationAsync(SelectedAnnotation);
    }

    public async Task ResizeSelectedAnnotationAsync(double newWidth, double newHeight)
    {
        if (SelectedAnnotation is null)
        {
            return;
        }

        SelectedAnnotation.Width = newWidth;
        SelectedAnnotation.Height = newHeight;
        await _annotationService.UpdateAnnotationAsync(SelectedAnnotation);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedAnnotation))]
    private async Task DeleteSelectedAnnotationAsync()
    {
        if (SelectedAnnotation is null)
        {
            return;
        }

        await _annotationService.DeleteAnnotationAsync(SelectedAnnotation);
        CurrentPageAnnotations.Remove(SelectedAnnotation);
        SelectedAnnotation = null;
    }

    private async Task LoadCurrentPageAsync()
    {
        CurrentPageImageBytes = await _pdfPageRenderer.RenderPageAsync(_filePath, CurrentPageIndex, _targetWidthPx, _targetHeightPx);

        SelectedAnnotation = null;
        CurrentPageAnnotations.Clear();
        if (_sheet is not null)
        {
            var annotations = await _annotationService.GetAnnotationsAsync(_sheet.Id, CurrentPageIndex);
            foreach (var annotation in annotations)
            {
                CurrentPageAnnotations.Add(annotation);
            }
        }
    }

    private async Task PersistLastViewedPageAsync()
    {
        if (_sheet is not null)
        {
            await _libraryService.UpdateSheetLastViewedPageAsync(_sheet, CurrentPageIndex);
        }
    }
}
```

Note what changed from the Plan 2 version: `LoadCurrentPageAsync` now also (re)loads `CurrentPageAnnotations` for whichever page it's rendering — this runs on initial load and on every `NextPageAsync`/`PreviousPageAsync`, so annotations always match the page currently on screen. `PlaceIconAsync` is a new `[RelayCommand]` taking a `string` parameter (CommunityToolkit.Mvvm generates `IAsyncRelayCommand<string> PlaceIconCommand` from an async method with one parameter).

- [ ] **Step 4: Register AnnotationService for DI**

In `src/PiccoloReader/MauiProgram.cs`, add alongside the other service registrations:

```csharp
builder.Services.AddSingleton<AnnotationService>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
Expected: all tests pass (total count goes from 44 to 51).

- [ ] **Step 6: Build for Android to verify DI wiring compiles**

Run: `dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 7: Commit**

```bash
git add src/PiccoloReader.Core/ViewModels/SheetViewerViewModel.cs src/PiccoloReader/MauiProgram.cs tests/PiccoloReader.Core.Tests/ViewModels/SheetViewerViewModelTests.cs
git commit -m "feat: wire AnnotationService into SheetViewerViewModel"
```

---

### Task 4: Tool panel UI + Music Icons picker

**Files:**
- Modify: `src/PiccoloReader/Views/SheetViewerPage.xaml`
- Modify: `src/PiccoloReader/Views/SheetViewerPage.xaml.cs`

**Interfaces:**
- Consumes: `SheetViewerViewModel.PlaceIconCommand`, `MusicIconCatalog.Categories` (Task 1/3).
- Produces: nothing new consumed by later tasks (Task 5/6 add to the page independently) — but this task's panel toggle button and layout structure must stay intact.

Only the "Music Icons" tab is built now — the architecture's Pencil/Eraser tabs are Plan 4 (roadmap explicitly scopes Eraser to Plan 4 alongside pencil strokes). Building a 3-tab switcher for two tabs that do nothing yet would just be thrown away; this task builds a single-purpose panel that Plan 4 extends with tabs.

**Icon rendering approach, updated after two rounds of on-device testing (code blocks below are the original attempt — see `SheetViewerPage.xaml`/`.xaml.cs` in the repo for what actually shipped):**

1. A `Label` with `FontFamily="Bravura"` renders every glyph as a blank/tofu box on Android — confirmed live on-device. Bravura is a CFF-flavored OTF, unlike the TTF fonts already working elsewhere in the app (Pacifico, MaterialOutlined); Android's `Label`/TextView text-layout can't resolve glyphs from it even though the font registers and builds without error.
2. Switching to `Image` + `FontImageSource` (the same mechanism already proven for the `MaterialOutlined` toolbar icons) fixed that — glyphs render correctly. But `FontImageSource.Size` turned out not to control the *visible* glyph size at all: confirmed by testing Size 28 through 72 on a clean install (ruling out caching) with zero visible difference. Bravura, like most music fonts, reserves a lot of vertical em-box space around each glyph's actual ink for staff-line alignment; `Size` scales that whole em-box (mostly whitespace) uniformly, and since the `Image` is fit into a fixed-size box regardless, the ink-to-whitespace ratio — and so the apparent size — never changes.
3. The actual fix: replace the `Image`/`FontImageSource` with an `SKCanvasView` (`SkiaSharp.Views.Maui.Controls`, already available — no new package) whose `PaintSurface` handler measures the glyph's own ink bounds via `SKFont.MeasureText` and scales/positions *that* to fill the box, ignoring the font's em-box entirely. This needed the Bravura `SKTypeface` loaded via `FileSystem.OpenAppPackageFileAsync("Bravura.otf")` (the `Resources/Raw` copy from Task 1) — pulled forward from Task 5, which was always going to need the same typeface for the same reason (on-page rendering has the identical em-box-padding problem). Task 5 should reuse the already-loaded `_bravuraTypeface` field/`EnsureBravuraTypefaceLoadedAsync()` method rather than re-adding them.

- [ ] **Step 1: Add the toolbar pen button and panel to SheetViewerPage.xaml**

Add a `ContentPage.ToolbarItems` block and the panel `Grid`, inserting into the existing page (the `Grid` that currently holds `PageImage`, the page indicator `Label`, and the `ActivityIndicator` gets a new sibling panel added after them, and a `ToolbarItems` block added before `ContentPage.Resources`):

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ContentPage
    x:Class="PiccoloReader.Views.SheetViewerPage"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:converters="clr-namespace:PiccoloReader.Converters"
    x:Name="ThisPage"
    Title="{Binding Title}">

    <ContentPage.ToolbarItems>
        <ToolbarItem Clicked="OnToggleToolPanelClicked">
            <ToolbarItem.IconImageSource>
                <FontImageSource Glyph="&#xE3C9;" FontFamily="MaterialOutlined" Size="24" Color="White" />
            </ToolbarItem.IconImageSource>
        </ToolbarItem>
    </ContentPage.ToolbarItems>

    <ContentPage.Resources>
        <converters:ByteArrayToImageSourceConverter x:Key="ByteArrayToImageSource" />
    </ContentPage.Resources>

    <Grid BackgroundColor="{AppThemeBinding Light={StaticResource White}, Dark={StaticResource Black}}">

        <Image
            x:Name="PageImage"
            Source="{Binding CurrentPageImageBytes, Converter={StaticResource ByteArrayToImageSource}}"
            Aspect="AspectFit"
            HorizontalOptions="Center"
            VerticalOptions="Center">
            <Image.GestureRecognizers>
                <TapGestureRecognizer Tapped="OnPageTapped" />
                <PinchGestureRecognizer PinchUpdated="OnPinchUpdated" />
                <PanGestureRecognizer PanUpdated="OnPanUpdated" />
            </Image.GestureRecognizers>
        </Image>

        <Label
            Text="{Binding PageIndicatorText}"
            VerticalOptions="End"
            HorizontalOptions="Center"
            Margin="0,0,0,16"
            Padding="12,6"
            BackgroundColor="{AppThemeBinding Light={StaticResource Secondary}, Dark={StaticResource Primary}}"
            TextColor="{StaticResource Tertiary}" />

        <ActivityIndicator
            IsRunning="{Binding IsLoading}"
            IsVisible="{Binding IsLoading}"
            HorizontalOptions="Center"
            VerticalOptions="Center" />

        <Border
            x:Name="ToolPanel"
            IsVisible="False"
            WidthRequest="260"
            HorizontalOptions="End"
            VerticalOptions="Fill"
            BackgroundColor="{AppThemeBinding Light={StaticResource Secondary}, Dark={StaticResource Primary}}"
            StrokeThickness="0">
            <ScrollView>
                <VerticalStackLayout Padding="12" Spacing="4">
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
                                                    <Image HorizontalOptions="Center" VerticalOptions="Center">
                                                        <Image.Source>
                                                            <FontImageSource
                                                                Glyph="{Binding Codepoint, Converter={StaticResource CodepointToGlyphString}}"
                                                                FontFamily="Bravura"
                                                                Size="34"
                                                                Color="{StaticResource Black}" />
                                                        </Image.Source>
                                                    </Image>
                                                </Border>
                                            </DataTemplate>
                                        </BindableLayout.ItemTemplate>
                                    </FlexLayout>
                                </VerticalStackLayout>
                            </DataTemplate>
                        </CollectionView.ItemTemplate>
                    </CollectionView>
                </VerticalStackLayout>
            </ScrollView>
        </Border>

    </Grid>
</ContentPage>
```

This references a `CodepointToGlyphString` converter (an `int` Unicode codepoint needs converting to the actual glyph character string for `Label.Text`) that isn't declared in `ContentPage.Resources` yet — add it next to the existing one:

```xml
<ContentPage.Resources>
    <converters:ByteArrayToImageSourceConverter x:Key="ByteArrayToImageSource" />
    <converters:CodepointToGlyphStringConverter x:Key="CodepointToGlyphString" />
</ContentPage.Resources>
```

- [ ] **Step 2: Write the CodepointToGlyphStringConverter**

```csharp
// src/PiccoloReader/Converters/CodepointToGlyphStringConverter.cs
using System.Globalization;

namespace PiccoloReader.Converters;

public class CodepointToGlyphStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int codepoint ? char.ConvertFromUtf32(codepoint) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

- [ ] **Step 3: Add the panel toggle and picker handlers to SheetViewerPage.xaml.cs**

Add a public `IconCategories` property (bound from the XAML above) and the two new event handlers. Insert into the existing class:

```csharp
using PiccoloReader.Core.Services;

// add this property near the top of the class, alongside `SheetId`:
public IReadOnlyList<MusicIconCategory> IconCategories => MusicIconCatalog.Categories;

// add these methods anywhere in the class body:
private void OnToggleToolPanelClicked(object? sender, EventArgs e)
{
    ToolPanel.IsVisible = !ToolPanel.IsVisible;
}

private async void OnIconPickerTapped(object? sender, TappedEventArgs e)
{
    if (e.Parameter is string iconKey)
    {
        await _viewModel.PlaceIconCommand.ExecuteAsync(iconKey);
    }
}
```

Add `using PiccoloReader.Core.Services;` to the top of the file if not already present.

- [ ] **Step 4: Build for Android and verify on-device**

Run: `dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android -t:Run`
Expected: builds, deploys, launches.

Manual check: open a sheet, tap the new toolbar pen icon — the panel slides into view on the right showing 4 category headers (Dynamics, Articulations, Fermata & Breath, Hairpins) each with their icon buttons rendered as real Bravura glyphs (not boxes/missing-glyph placeholders — if a box/tofu glyph appears, the font isn't loading correctly, stop and fix before continuing). Tap a few different icon buttons from different categories and confirm no crash (the icon won't be *visible* on the page yet — that's Task 5 — but `PlaceIconCommand` should run without error; check logcat for exceptions).

- [ ] **Step 5: Commit**

```bash
git add src/PiccoloReader/Views/SheetViewerPage.xaml src/PiccoloReader/Views/SheetViewerPage.xaml.cs src/PiccoloReader/Converters/CodepointToGlyphStringConverter.cs
git commit -m "feat: add tool panel with Music Icons picker"
```

---

### Task 5: Render placed icons on the page

**Files:**
- Modify: `src/PiccoloReader/Views/SheetViewerPage.xaml`
- Modify: `src/PiccoloReader/Views/SheetViewerPage.xaml.cs`

**Interfaces:**
- Consumes: `SheetViewerViewModel.CurrentPageAnnotations` (Task 3), `MusicIconCatalog.FindByKey` (Task 1).
- Produces: the `PageContainer` element and the transform-on-container pattern that Task 6's selection overlay positions itself against.

This task does a structural refactor: `PageImage`'s pinch/pan/tap transform code (built across Plan 2 and its follow-up fixes) currently reads and writes `PageImage.Scale`/`TranslationX`/`TranslationY`/`AnchorX`/`AnchorY`/`Width`/`Height`/`X`/`Y` directly. Per the architecture spec, annotations must move/scale *together* with the page ("a single transform... applied to both layers together"), so both the page image and the new annotation canvas need to sit inside one shared container that the transform applies to, instead of applying it to `PageImage` alone.

- [ ] **Step 1: Wrap PageImage and add the annotation canvas in SheetViewerPage.xaml**

Replace the `<Image x:Name="PageImage" ...>` block (gesture recognizers included) with a wrapping container holding both the image and a new `SKCanvasView`:

```xml
xmlns:skia="clr-namespace:SkiaSharp.Views.Maui.Controls;assembly=SkiaSharp.Views.Maui.Controls"
```

(add this namespace to the `<ContentPage>` root tag, alongside the existing `xmlns:converters` etc.)

```xml
<Grid
    x:Name="PageContainer"
    HorizontalOptions="Center"
    VerticalOptions="Center">
    <Grid.GestureRecognizers>
        <TapGestureRecognizer Tapped="OnPageTapped" />
        <PinchGestureRecognizer PinchUpdated="OnPinchUpdated" />
        <PanGestureRecognizer PanUpdated="OnPanUpdated" />
    </Grid.GestureRecognizers>

    <Image
        x:Name="PageImage"
        Source="{Binding CurrentPageImageBytes, Converter={StaticResource ByteArrayToImageSource}}"
        Aspect="AspectFit" />

    <skia:SKCanvasView x:Name="AnnotationCanvas" PaintSurface="OnAnnotationCanvasPaintSurface" />
</Grid>
```

`PageContainer` replaces `PageImage` as the gesture target and the element the zoom/pan/reset code manipulates (Step 2). `PageImage` itself keeps only its `Source`/`Aspect` — no more `HorizontalOptions`/`VerticalOptions` (the parent `Grid` now centers everything) and no more gesture recognizers.

- [ ] **Step 2: Update SheetViewerPage.xaml.cs to target PageContainer instead of PageImage**

Every reference to `PageImage.Scale`, `PageImage.TranslationX`, `PageImage.TranslationY`, `PageImage.AnchorX`, `PageImage.AnchorY`, `PageImage.Width`, `PageImage.Height`, `PageImage.X`, `PageImage.Y` becomes `PageContainer.*` instead — the gesture math itself is unchanged, only which element it reads/writes. Full new content of `src/PiccoloReader/Views/SheetViewerPage.xaml.cs`:

```csharp
using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace PiccoloReader.Views;

[QueryProperty(nameof(SheetId), "sheetId")]
public partial class SheetViewerPage : ContentPage
{
    private const double ZoomedInThreshold = 1.05;
    private const double PageTurnDragThreshold = 60;

    private readonly SheetViewerViewModel _viewModel;

    private double _startScale = 1;
    private double _currentScale = 1;
    private double _xOffset;
    private double _yOffset;
    private double _panTotalX;

    private SKTypeface? _bravuraTypeface;

    public SheetViewerPage(SheetViewerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;

        _viewModel.CurrentPageAnnotations.CollectionChanged += (_, _) => AnnotationCanvas.InvalidateSurface();
    }

    public string SheetId { get; set; } = string.Empty;

    public IReadOnlyList<MusicIconCategory> IconCategories => MusicIconCatalog.Categories;

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await EnsureBravuraTypefaceLoadedAsync();

        if (!int.TryParse(SheetId, out var sheetId))
        {
            return;
        }

        var displayInfo = DeviceDisplay.Current.MainDisplayInfo;
        var targetWidthPx = (int)displayInfo.Width;
        var targetHeightPx = (int)displayInfo.Height;

        await _viewModel.LoadAsync(sheetId, targetWidthPx, targetHeightPx);
        ResetZoom();
    }

    private async Task EnsureBravuraTypefaceLoadedAsync()
    {
        if (_bravuraTypeface is not null)
        {
            return;
        }

        using var stream = await FileSystem.OpenAppPackageFileAsync("Bravura.otf");
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;
        _bravuraTypeface = SKTypeface.FromStream(memoryStream);
    }

    private void ResetZoom()
    {
        _startScale = 1;
        _currentScale = 1;
        _xOffset = 0;
        _yOffset = 0;
        PageContainer.Scale = 1;
        PageContainer.TranslationX = 0;
        PageContainer.TranslationY = 0;
    }

    private void OnPageTapped(object? sender, TappedEventArgs e)
    {
        if (_currentScale > ZoomedInThreshold)
        {
            // Ignore tap-to-turn while zoomed in - the user is most likely
            // trying to look around the page, not turn it.
            return;
        }

        var position = e.GetPosition(PageContainer);
        if (position is null)
        {
            return;
        }

        if (position.Value.X < PageContainer.Width / 2)
        {
            TryGoToPreviousPage();
        }
        else
        {
            TryGoToNextPage();
        }
    }

    // Pinch-to-zoom. On Android, PinchGestureHandler.OnPinch (MAUI source,
    // src/Controls/src/Core/Platform/Android/PinchGestureHandler.cs)
    // already computes e.Scale as `1 + (rawFrameDelta - 1) * viewScaleAtGestureStart`
    // before it ever reaches this handler - i.e. e.Scale-1 is already
    // scaled by the view's starting scale. Only the delta needs adding,
    // no extra multiplication (see PR #25 for the full explanation).
    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status == GestureStatus.Started)
        {
            _startScale = PageContainer.Scale;
            PageContainer.AnchorX = 0;
            PageContainer.AnchorY = 0;
        }
        else if (e.Status == GestureStatus.Running)
        {
            _currentScale += e.Scale - 1;
            _currentScale = Math.Max(1, _currentScale);

            var renderedX = PageContainer.X + _xOffset;
            var deltaX = renderedX / PageContainer.Width;
            var deltaWidth = PageContainer.Width / (PageContainer.Width * _startScale);
            var originX = (e.ScaleOrigin.X - deltaX) * deltaWidth;

            var renderedY = PageContainer.Y + _yOffset;
            var deltaY = renderedY / PageContainer.Height;
            var deltaHeight = PageContainer.Height / (PageContainer.Height * _startScale);
            var originY = (e.ScaleOrigin.Y - deltaY) * deltaHeight;

            var targetX = _xOffset - (originX * PageContainer.Width * (_currentScale - _startScale));
            var targetY = _yOffset - (originY * PageContainer.Height * (_currentScale - _startScale));

            PageContainer.TranslationX = Math.Clamp(targetX, -PageContainer.Width * (_currentScale - 1), 0);
            PageContainer.TranslationY = Math.Clamp(targetY, -PageContainer.Height * (_currentScale - 1), 0);
            PageContainer.Scale = _currentScale;
        }
        else if (e.Status is GestureStatus.Completed or GestureStatus.Canceled)
        {
            _xOffset = PageContainer.TranslationX;
            _yOffset = PageContainer.TranslationY;
        }
    }

    // Handles both panning around a zoomed-in page and swipe-to-turn-pages
    // when not zoomed (see PR #23 for why this replaced a separate
    // SwipeGestureRecognizer - Pan+Swipe on one element conflict on Android).
    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Running:
                if (_currentScale > ZoomedInThreshold)
                {
                    PageContainer.TranslationX = Math.Clamp(_xOffset + e.TotalX, -PageContainer.Width * (_currentScale - 1), 0);
                    PageContainer.TranslationY = Math.Clamp(_yOffset + e.TotalY, -PageContainer.Height * (_currentScale - 1), 0);
                }

                _panTotalX = e.TotalX;
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                if (_currentScale > ZoomedInThreshold)
                {
                    _xOffset = PageContainer.TranslationX;
                    _yOffset = PageContainer.TranslationY;
                }
                else if (_panTotalX <= -PageTurnDragThreshold)
                {
                    TryGoToNextPage();
                }
                else if (_panTotalX >= PageTurnDragThreshold)
                {
                    TryGoToPreviousPage();
                }

                _panTotalX = 0;
                break;
        }
    }

    private void TryGoToNextPage()
    {
        if (_viewModel.NextPageCommand.CanExecute(null))
        {
            _viewModel.NextPageCommand.Execute(null);
            ResetZoom();
        }
    }

    private void TryGoToPreviousPage()
    {
        if (_viewModel.PreviousPageCommand.CanExecute(null))
        {
            _viewModel.PreviousPageCommand.Execute(null);
            ResetZoom();
        }
    }

    private void OnToggleToolPanelClicked(object? sender, EventArgs e)
    {
        ToolPanel.IsVisible = !ToolPanel.IsVisible;
    }

    private async void OnIconPickerTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string iconKey)
        {
            await _viewModel.PlaceIconCommand.ExecuteAsync(iconKey);
        }
    }

    private void OnAnnotationCanvasPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        if (_bravuraTypeface is null)
        {
            return;
        }

        var info = e.Info;

        foreach (var annotation in _viewModel.CurrentPageAnnotations)
        {
            var icon = MusicIconCatalog.FindByKey(annotation.IconKey);
            if (icon is null)
            {
                continue;
            }

            using var font = new SKFont(_bravuraTypeface, (float)(annotation.Height * info.Height));
            using var paint = new SKPaint
            {
                Color = SKColors.Black,
                IsAntialias = true
            };

            var x = (float)(annotation.X * info.Width);
            var y = (float)((annotation.Y + annotation.Height) * info.Height);

            canvas.DrawText(char.ConvertFromUtf32(icon.Codepoint), x, y, font, paint);
        }
    }
}
```

Note: `OnToggleToolPanelClicked` and `OnIconPickerTapped` from Task 4 are included here since this step replaces the whole file — don't lose them.

`_viewModel.CurrentPageAnnotations.CollectionChanged` triggers a repaint whenever an icon is added/removed (`ObservableCollection<T>` implements `INotifyCollectionChanged`); this is wired in the constructor since `InitializeComponent()` must run first for `AnnotationCanvas` to exist.

- [ ] **Step 3: Build for Android and verify on-device**

Run: `dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android -t:Run`
Expected: builds, deploys.

Manual check: open a sheet, open the tool panel, tap a Dynamics icon (e.g. "f") — it should now actually appear centered on the page as a real Bravura glyph. Place a couple more from different categories, confirm they all render. Pinch-zoom and pan — confirm the placed icons move/scale *with* the page (not left behind or misaligned). Turn to another page and back — confirm icons from the first page are gone on the new page and reappear correctly when you return (this exercises Task 3's per-page reload). No crashes in logcat.

- [ ] **Step 4: Commit**

```bash
git add src/PiccoloReader/Views/SheetViewerPage.xaml src/PiccoloReader/Views/SheetViewerPage.xaml.cs
git commit -m "feat: render placed icon annotations on an SKCanvasView overlay"
```

---

### Task 6: Select, move, resize, and delete placed icons

**Files:**
- Modify: `src/PiccoloReader/Views/SheetViewerPage.xaml`
- Modify: `src/PiccoloReader/Views/SheetViewerPage.xaml.cs`

**Interfaces:**
- Consumes: `SheetViewerViewModel.SelectedAnnotation`, `.MoveSelectedAnnotationAsync`, `.ResizeSelectedAnnotationAsync`, `.DeleteSelectedAnnotationCommand` (Task 3); `PageContainer` (Task 5).
- Produces: nothing further — last task in this plan.

Selection interaction is a small overlay (border + resize handle + delete button) positioned over the selected icon, with its *own* pan gesture recognizers — not added to `PageContainer` (which already carries pinch/pan/tap for page zoom and turning; stacking a fourth gesture recognizer there risks the same kind of gesture-arena conflict already hit twice in Plan 2's follow-up fixes). The overlay only exists/is interactive while something is selected.

- [ ] **Step 1: Add the selection overlay and tap-to-select to SheetViewerPage.xaml**

Add inside `PageContainer`, after `AnnotationCanvas`, and add a `Tapped` handler to `AnnotationCanvas` itself for hit-testing:

```xml
<skia:SKCanvasView x:Name="AnnotationCanvas" PaintSurface="OnAnnotationCanvasPaintSurface">
    <skia:SKCanvasView.GestureRecognizers>
        <TapGestureRecognizer Tapped="OnAnnotationCanvasTapped" />
    </skia:SKCanvasView.GestureRecognizers>
</skia:SKCanvasView>

<Grid x:Name="SelectionOverlay" IsVisible="False">
    <Border
        x:Name="SelectionBorder"
        Stroke="{StaticResource Primary}"
        StrokeThickness="2"
        BackgroundColor="Transparent"
        InputTransparent="False">
        <Border.GestureRecognizers>
            <PanGestureRecognizer PanUpdated="OnSelectionMovePanUpdated" />
        </Border.GestureRecognizers>
    </Border>

    <BoxView
        x:Name="ResizeHandle"
        WidthRequest="24"
        HeightRequest="24"
        CornerRadius="12"
        Color="{StaticResource Primary}"
        HorizontalOptions="Start"
        VerticalOptions="Start">
        <BoxView.GestureRecognizers>
            <PanGestureRecognizer PanUpdated="OnSelectionResizePanUpdated" />
        </BoxView.GestureRecognizers>
    </BoxView>

    <ImageButton
        x:Name="DeleteButton"
        WidthRequest="28"
        HeightRequest="28"
        CornerRadius="14"
        BackgroundColor="{StaticResource Primary}"
        HorizontalOptions="Start"
        VerticalOptions="Start"
        Clicked="OnDeleteSelectedIconClicked">
        <ImageButton.Source>
            <FontImageSource Glyph="&#xE5CD;" FontFamily="MaterialOutlined" Size="16" Color="White" />
        </ImageButton.Source>
    </ImageButton>
</Grid>
```

`SelectionBorder` fills the whole `SelectionOverlay` Grid cell (sized/positioned in code-behind, Step 3) and handles move-by-drag; `ResizeHandle` sits at its own corner (positioned relative to the same bounds) and handles resize-by-drag; `DeleteButton` sits at the opposite corner.

- [ ] **Step 2: Wire tap-to-select and the three new gesture handlers in SheetViewerPage.xaml.cs**

Add to the class (these read the same `PageContainer`-relative coordinate conventions as the existing gesture code — a tap/pan position from `e.GetPosition(PageContainer)` divided by `PageContainer.Width`/`Height` gives the normalized 0–1 position used throughout the data model):

```csharp
private double _resizeStartWidth;
private double _resizeStartHeight;
private double _moveStartX;
private double _moveStartY;

private void OnAnnotationCanvasTapped(object? sender, TappedEventArgs e)
{
    var position = e.GetPosition(PageContainer);
    if (position is null || PageContainer.Width <= 0 || PageContainer.Height <= 0)
    {
        return;
    }

    var normalizedX = position.Value.X / PageContainer.Width;
    var normalizedY = position.Value.Y / PageContainer.Height;

    var hit = _viewModel.CurrentPageAnnotations.FirstOrDefault(a =>
        normalizedX >= a.X && normalizedX <= a.X + a.Width &&
        normalizedY >= a.Y && normalizedY <= a.Y + a.Height);

    _viewModel.SelectedAnnotation = hit;
    UpdateSelectionOverlay();
}

private void UpdateSelectionOverlay()
{
    var annotation = _viewModel.SelectedAnnotation;
    if (annotation is null || PageContainer.Width <= 0 || PageContainer.Height <= 0)
    {
        SelectionOverlay.IsVisible = false;
        return;
    }

    SelectionOverlay.IsVisible = true;

    var left = annotation.X * PageContainer.Width;
    var top = annotation.Y * PageContainer.Height;
    var width = annotation.Width * PageContainer.Width;
    var height = annotation.Height * PageContainer.Height;

    AbsoluteLayout.SetLayoutBounds(SelectionOverlay, new Rect(left, top, width, height));
    AbsoluteLayout.SetLayoutFlags(SelectionOverlay, AbsoluteLayoutFlags.None);

    ResizeHandle.TranslationX = width - ResizeHandle.WidthRequest / 2;
    ResizeHandle.TranslationY = height - ResizeHandle.HeightRequest / 2;

    DeleteButton.TranslationX = width - DeleteButton.WidthRequest / 2;
    DeleteButton.TranslationY = -DeleteButton.HeightRequest / 2;
}

private void OnSelectionMovePanUpdated(object? sender, PanUpdatedEventArgs e)
{
    var annotation = _viewModel.SelectedAnnotation;
    if (annotation is null || PageContainer.Width <= 0 || PageContainer.Height <= 0)
    {
        return;
    }

    switch (e.StatusType)
    {
        case GestureStatus.Started:
            _moveStartX = annotation.X;
            _moveStartY = annotation.Y;
            break;

        case GestureStatus.Running:
            var newX = _moveStartX + e.TotalX / PageContainer.Width;
            var newY = _moveStartY + e.TotalY / PageContainer.Height;
            annotation.X = newX;
            annotation.Y = newY;
            UpdateSelectionOverlay();
            AnnotationCanvas.InvalidateSurface();
            break;

        case GestureStatus.Completed:
        case GestureStatus.Canceled:
            _ = _viewModel.MoveSelectedAnnotationAsync(annotation.X, annotation.Y);
            break;
    }
}

private void OnSelectionResizePanUpdated(object? sender, PanUpdatedEventArgs e)
{
    var annotation = _viewModel.SelectedAnnotation;
    if (annotation is null || PageContainer.Width <= 0 || PageContainer.Height <= 0)
    {
        return;
    }

    switch (e.StatusType)
    {
        case GestureStatus.Started:
            _resizeStartWidth = annotation.Width;
            _resizeStartHeight = annotation.Height;
            break;

        case GestureStatus.Running:
            var newWidth = Math.Max(0.02, _resizeStartWidth + e.TotalX / PageContainer.Width);
            var newHeight = Math.Max(0.02, _resizeStartHeight + e.TotalY / PageContainer.Height);
            annotation.Width = newWidth;
            annotation.Height = newHeight;
            UpdateSelectionOverlay();
            AnnotationCanvas.InvalidateSurface();
            break;

        case GestureStatus.Completed:
        case GestureStatus.Canceled:
            _ = _viewModel.ResizeSelectedAnnotationAsync(annotation.Width, annotation.Height);
            break;
    }
}

private async void OnDeleteSelectedIconClicked(object? sender, EventArgs e)
{
    await _viewModel.DeleteSelectedAnnotationCommand.ExecuteAsync(null);
    UpdateSelectionOverlay();
    AnnotationCanvas.InvalidateSurface();
}
```

`AbsoluteLayout.SetLayoutBounds`/`SetLayoutFlags` need `PageContainer` to actually be (or contain) an `AbsoluteLayout` for those attached properties to take effect. Update Task 5's `PageContainer` from a `Grid` to an `AbsoluteLayout` in `SheetViewerPage.xaml` (`PageImage` and `AnnotationCanvas` then need `AbsoluteLayout.LayoutBounds="0,0,1,1"` and `AbsoluteLayout.LayoutFlags="All"` to fill it, matching the previous `Grid`-implied fill behavior):

```xml
<AbsoluteLayout
    x:Name="PageContainer"
    HorizontalOptions="Center"
    VerticalOptions="Center">
    <AbsoluteLayout.GestureRecognizers>
        <TapGestureRecognizer Tapped="OnPageTapped" />
        <PinchGestureRecognizer PinchUpdated="OnPinchUpdated" />
        <PanGestureRecognizer PanUpdated="OnPanUpdated" />
    </AbsoluteLayout.GestureRecognizers>

    <Image
        x:Name="PageImage"
        Source="{Binding CurrentPageImageBytes, Converter={StaticResource ByteArrayToImageSource}}"
        Aspect="AspectFit"
        AbsoluteLayout.LayoutBounds="0,0,1,1"
        AbsoluteLayout.LayoutFlags="All" />

    <skia:SKCanvasView
        x:Name="AnnotationCanvas"
        PaintSurface="OnAnnotationCanvasPaintSurface"
        AbsoluteLayout.LayoutBounds="0,0,1,1"
        AbsoluteLayout.LayoutFlags="All">
        <skia:SKCanvasView.GestureRecognizers>
            <TapGestureRecognizer Tapped="OnAnnotationCanvasTapped" />
        </skia:SKCanvasView.GestureRecognizers>
    </skia:SKCanvasView>

    <Grid x:Name="SelectionOverlay" IsVisible="False">
        <!-- SelectionBorder / ResizeHandle / DeleteButton exactly as in Step 1 -->
    </Grid>
</AbsoluteLayout>
```

- [ ] **Step 3: Recompute the selection overlay whenever the page transform changes**

The overlay's position (Step 2's `UpdateSelectionOverlay`) is computed from `PageContainer.Width`/`Height` in *unscaled* logical units — since `SelectionOverlay` lives inside `PageContainer` via `AbsoluteLayout`, it automatically inherits `PageContainer`'s `Scale`/`TranslationX`/`TranslationY` the same way `PageImage` and `AnnotationCanvas` do, so it doesn't need separate transform handling. Call `UpdateSelectionOverlay()` after `ResetZoom()` (page turns should clear/hide the selection, since `LoadCurrentPageAsync` already clears `SelectedAnnotation` on every page change) — add the call at the end of `ResetZoom()`:

```csharp
private void ResetZoom()
{
    _startScale = 1;
    _currentScale = 1;
    _xOffset = 0;
    _yOffset = 0;
    PageContainer.Scale = 1;
    PageContainer.TranslationX = 0;
    PageContainer.TranslationY = 0;
    UpdateSelectionOverlay();
}
```

- [ ] **Step 4: Build for Android and verify on-device**

Run: `dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android -t:Run`
Expected: builds, deploys.

Manual check, on a real placed icon:
1. Tap a placed icon — a border with a resize handle (bottom-right) and delete button (top-right) appears around it.
2. Drag the border — the icon moves with your finger, stays selected.
3. Drag the resize handle — the icon grows/shrinks.
4. Tap the delete button — the icon disappears, selection overlay hides.
5. Tap empty page space — selection clears (no crash from `hit == null`).
6. Place an icon, turn the page, turn back — icon is still there (persisted), not selected (selection doesn't carry across page changes).
7. Close and reopen the sheet (back to Library, tap sheet again) — placed icons are still there, in the same position/size.

Check logcat for crashes throughout.

- [ ] **Step 5: Run the full Core test suite one more time**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
Expected: all 51 tests pass (unchanged from Task 3 — this task touches no Core code).

- [ ] **Step 6: Commit**

```bash
git add src/PiccoloReader/Views/SheetViewerPage.xaml src/PiccoloReader/Views/SheetViewerPage.xaml.cs
git commit -m "feat: select, move, resize, and delete placed icon annotations"
```

- [ ] **Step 7: Update the roadmap**

In `docs/superpowers/plans/roadmap.md`, add a "Plan 3: Icon Annotations" task table (same shape as Plan 2's), filling in PR links as each task's PR is opened/merged during execution.

---

## Post-Plan Notes

- **mp/mf dynamics are not in this plan.** Bravura has no single glyph for them (SMuFL composes them from `dynamicMezzo` + `dynamicPiano`/`dynamicForte`). Adding them means teaching both the picker button and the `OnAnnotationCanvasPaintSurface` renderer to draw two glyphs side by side as one logical icon — a real but contained follow-up once the single-glyph system here is proven.
- **The Eraser tool is not in this plan** (Plan 4, per the roadmap, alongside pencil strokes) — deletion in this plan is only via the selection overlay's delete button. `requirements.md` describes the Eraser as *the* deletion mechanism for icons ("eraser deletes the whole icon"); this plan's delete button is a simpler, immediate alternative that satisfies the roadmap's explicit "...delete icons..." scope for Plan 3 without waiting on Plan 4's broader Eraser (which also needs to hit-test pencil strokes that don't exist yet). Plan 4's Eraser becomes an *additional* way to delete both icons and strokes, not a replacement for this button.
- **Drag-from-right-edge to open the tool panel is not in this plan.** Only the toolbar pen-button toggle is built. The architecture spec mentions both; the edge-swipe gesture would be a fifth gesture recognizer competing for the same touch surface already carrying tap/pinch/pan for zoom and page-turning — deliberately deferred rather than risking another gesture-arena conflict.
- **Undo/redo (Plan 5) is not in this plan.** Every placement/move/resize/delete in this plan commits directly and immediately (matching the existing page-navigation persistence pattern) — Plan 5 will need to introduce a command-object stack that wraps these same `AnnotationService` calls, per the architecture spec's undo/redo design.
- **Categories are always-expanded, not true collapsible accordions.** `requirements.md` describes "a list of accordions... each accordion expands to show that category's icons," implying collapse/expand-on-tap. Task 4 instead shows all 4 categories' icons at once — with only 13 icons total across them, an always-visible list is compact enough to not need progressive disclosure, and it avoids building per-category expand/collapse state for a v1. If the icon set grows enough that this gets visually crowded, revisit then.
