# PDF Viewer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user tap a sheet in the Library or Folder page and read it — a Sheet Viewer screen that rasterizes PDF pages via native Android APIs, with page-by-page navigation and pinch-zoom/pan.

**Architecture:** A single `IPdfPageRenderer` interface (Core) hides all PDF rendering behind two methods — get page count, render one page to PNG bytes — with one real implementation (Android, `android.graphics.pdf.PdfRenderer`) registered through MAUI's DI. `SheetViewerViewModel` (Core, fully unit-testable against a fake renderer) owns the current page index and drives the renderer. `SheetViewerPage` (Presentation) displays the current page as an `Image`, handles tap/swipe to turn pages and pinch/pan to zoom, and is reached via a new `sheetviewer` Shell route from both the Library and Folder pages.

**Tech Stack:** .NET MAUI (net10.0-android target only for this plan — see Scope below), CommunityToolkit.Mvvm (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`), sqlite-net-pcl, native MAUI gesture recognizers (`TapGestureRecognizer`, `SwipeGestureRecognizer`, `PinchGestureRecognizer`, `PanGestureRecognizer` — no new package needed).

**Spec:** [`docs/superpowers/specs/2026-09-14-architecture-design.md`](../specs/2026-09-14-architecture-design.md) — see "PDF Rendering & Annotation Overlay" and "Navigation & Screens" sections. This plan implements the PDF rendering and page navigation/zoom pieces of that design; the annotation overlay (icons, pencil, eraser) described in the same section is explicitly **out of scope** — that's Plans 3 and 4.

## Scope

**Android only.** The architecture calls for both an Android and an iOS `IPdfPageRenderer` implementation, but this environment has no Mac/iOS simulator to build or verify against — even CI can't compile the iOS target (confirmed while building the `build-apk.yml` workflow: `maui-ios` workload install fails outright on the Linux runner). Writing an untestable iOS implementation would just ship unverified, possibly-broken code. So:

- `IPdfPageRenderer` (the interface) is written now, platform-agnostic, in Core.
- The Android implementation is written and verified on-device/emulator now.
- iOS gets a stub file (`NotImplementedException`) so the project still compiles for `net10.0-ios` — the real iOS implementation is deferred to whenever there's a way to test it.

## Global Constraints

- Target frameworks stay `net10.0-android;net10.0-ios` in the `.csproj` (don't narrow it) — only the *implementation* is Android-only, per Scope above.
- `Microsoft.Maui.Controls` version floor is 10.0.20 — don't add any package that raises it.
- No new NuGet packages needed for this plan — all gestures used are built into MAUI core.
- Follow existing MVVM pattern exactly: `ObservableObject` + `[ObservableProperty]` + `[RelayCommand]` from CommunityToolkit.Mvvm (see `FolderViewModel` for the reference style).
- Core project (`PiccoloReader.Core`) stays platform-agnostic — no `Microsoft.Maui.*` or `Android.*` references in it. Platform code lives only under `src/PiccoloReader/Platforms/<Platform>/`.
- One task = one PR. Wait for explicit merge confirmation before starting the next task's branch (standing project convention).
- Every PR that touches app code needs a Release APK built and either sent (if under the ~30 MiB chat-attachment limit) or its local path reported, verified to launch first.

---

### Task 1: IPdfPageRenderer interface + Android implementation + DI registration

**Files:**
- Create: `src/PiccoloReader.Core/Services/IPdfPageRenderer.cs`
- Create: `src/PiccoloReader/Platforms/Android/PdfPageRenderer.cs`
- Create: `src/PiccoloReader/Platforms/iOS/PdfPageRenderer.cs`
- Modify: `src/PiccoloReader/MauiProgram.cs` (DI registration)

**Interfaces:**
- Produces: `PiccoloReader.Core.Services.IPdfPageRenderer` with:
  - `Task<int> GetPageCountAsync(string filePath)`
  - `Task<byte[]> RenderPageAsync(string filePath, int pageIndex, int targetWidthPx, int targetHeightPx)` — `pageIndex` is **0-based**. `targetWidthPx`/`targetHeightPx` are a *bounding box*, not a forced size — the implementation must preserve the PDF page's own aspect ratio and fit within the box (a page rendered to a stretched/wrong-aspect bitmap would look distorted). Returns PNG-encoded bytes.
- Consumes: nothing from earlier tasks (this is the first task).

This task has no unit tests of its own — `IPdfPageRenderer` is an interface with no logic, and its real implementation talks to `android.graphics.pdf.PdfRenderer`, a platform API that can't run under the `net10.0` test project (same reasoning as the architecture spec's Testing Approach section: "the two `IPdfPageRenderer` implementations are thin adapters over OS APIs, verified via manual/on-device testing rather than unit tests"). It's exercised by later tasks' fakes and verified end-to-end manually in Task 5.

- [ ] **Step 1: Write the interface**

```csharp
// src/PiccoloReader.Core/Services/IPdfPageRenderer.cs
namespace PiccoloReader.Core.Services;

public interface IPdfPageRenderer
{
    Task<int> GetPageCountAsync(string filePath);

    Task<byte[]> RenderPageAsync(string filePath, int pageIndex, int targetWidthPx, int targetHeightPx);
}
```

- [ ] **Step 2: Write the Android implementation**

```csharp
// src/PiccoloReader/Platforms/Android/PdfPageRenderer.cs
using Android.Graphics;
using Android.Graphics.Pdf;
using Android.OS;
using PiccoloReader.Core.Services;
using Java.IO;

namespace PiccoloReader.Platforms.Android;

public class PdfPageRenderer : IPdfPageRenderer
{
    public Task<int> GetPageCountAsync(string filePath) => Task.Run(() =>
    {
        using var descriptor = ParcelFileDescriptor.Open(new File(filePath), ParcelFileMode.ReadOnly);
        using var renderer = new global::Android.Graphics.Pdf.PdfRenderer(descriptor!);
        return renderer.PageCount;
    });

    public Task<byte[]> RenderPageAsync(string filePath, int pageIndex, int targetWidthPx, int targetHeightPx) => Task.Run(() =>
    {
        using var descriptor = ParcelFileDescriptor.Open(new File(filePath), ParcelFileMode.ReadOnly);
        using var renderer = new global::Android.Graphics.Pdf.PdfRenderer(descriptor!);
        using var page = renderer.OpenPage(pageIndex);

        var scale = Math.Min((float)targetWidthPx / page.Width, (float)targetHeightPx / page.Height);
        var bitmapWidth = Math.Max(1, (int)(page.Width * scale));
        var bitmapHeight = Math.Max(1, (int)(page.Height * scale));

        using var bitmap = Bitmap.CreateBitmap(bitmapWidth, bitmapHeight, Bitmap.Config.Argb8888!);
        bitmap.EraseColor(global::Android.Graphics.Color.White);
        page.Render(bitmap, null, null, PdfRenderMode.ForDisplay);

        using var stream = new MemoryStream();
        bitmap.Compress(Bitmap.CompressFormat.Png!, 100, stream);
        return stream.ToArray();
    });
}
```

Android SDK binding method/property names (`PageCount`, `OpenPage`, `Page.Width`/`Height`, `Render` overload, `PdfRenderMode.ForDisplay`, `Bitmap.CreateBitmap`, `Bitmap.Config.Argb8888`, `Bitmap.CompressFormat.Png`) are written from Android SDK knowledge — if any name doesn't match what the installed Android bindings expose, the build error will name the correct one; fix and continue rather than guessing further.

- [ ] **Step 3: Write the iOS stub**

```csharp
// src/PiccoloReader/Platforms/iOS/PdfPageRenderer.cs
using PiccoloReader.Core.Services;

namespace PiccoloReader.Platforms.iOS;

// Deferred: no Mac/iOS simulator available to build or test a real PDFKit
// implementation against (see "Scope" in the PDF Viewer plan). This stub
// exists only so the app still compiles for net10.0-ios.
public class PdfPageRenderer : IPdfPageRenderer
{
    public Task<int> GetPageCountAsync(string filePath) =>
        throw new NotImplementedException("iOS PDF rendering is not implemented yet.");

    public Task<byte[]> RenderPageAsync(string filePath, int pageIndex, int targetWidthPx, int targetHeightPx) =>
        throw new NotImplementedException("iOS PDF rendering is not implemented yet.");
}
```

- [ ] **Step 4: Register per-platform in MauiProgram.cs**

Open `src/PiccoloReader/MauiProgram.cs` and add, near the other `builder.Services.AddSingleton` calls:

```csharp
#if ANDROID
builder.Services.AddSingleton<IPdfPageRenderer, PiccoloReader.Platforms.Android.PdfPageRenderer>();
#elif IOS
builder.Services.AddSingleton<IPdfPageRenderer, PiccoloReader.Platforms.iOS.PdfPageRenderer>();
#endif
```

Add `using PiccoloReader.Core.Services;` if not already present (it already is, for `IAppStorageProvider`).

- [ ] **Step 5: Build for Android to verify it compiles**

Run: `dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add src/PiccoloReader.Core/Services/IPdfPageRenderer.cs src/PiccoloReader/Platforms/Android/PdfPageRenderer.cs src/PiccoloReader/Platforms/iOS/PdfPageRenderer.cs src/PiccoloReader/MauiProgram.cs
git commit -m "feat: add IPdfPageRenderer with Android implementation"
```

---

### Task 2: LibraryService — fetch a sheet by id, persist a computed page count

**Files:**
- Modify: `src/PiccoloReader.Core/Services/LibraryService.cs`
- Test: `tests/PiccoloReader.Core.Tests/Services/LibraryServiceTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces:
  - `Task<Sheet> GetSheetAsync(int sheetId)` — throws if not found (matches existing `DeleteFolderAsync`'s use of `.FirstAsync()`, which throws `InvalidOperationException` on no match; that's consistent with the rest of this service, so keep it).
  - `Task UpdateSheetPageCountAsync(Sheet sheet, int pageCount)` — sets `sheet.PageCount` and persists it.
  - Both consumed by `SheetViewerViewModel` in Task 3.

`PdfImportService` still always inserts sheets with `PageCount = 0` (unchanged) — the real page count gets filled in lazily the first time a sheet is opened in the viewer (Task 3), not at import time. This keeps Task 1/2 fully decoupled from `PdfImportService` and its existing tests.

- [ ] **Step 1: Write the failing tests**

Add to `tests/PiccoloReader.Core.Tests/Services/LibraryServiceTests.cs` (same file, same `InsertSheetAsync` test helper already used by the other tests in that file):

```csharp
[Fact]
public async Task GetSheetAsync_ReturnsMatchingSheet()
{
    var sheet = await InsertSheetAsync(null);

    var result = await _sut.GetSheetAsync(sheet.Id);

    Assert.Equal(sheet.Id, result.Id);
    Assert.Equal(sheet.Title, result.Title);
}

[Fact]
public async Task UpdateSheetPageCountAsync_PersistsPageCount()
{
    var sheet = await InsertSheetAsync(null);

    await _sut.UpdateSheetPageCountAsync(sheet, 12);

    var reloaded = await _sut.GetSheetAsync(sheet.Id);
    Assert.Equal(12, reloaded.PageCount);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter "GetSheetAsync_ReturnsMatchingSheet|UpdateSheetPageCountAsync_PersistsPageCount"`
Expected: FAIL — `LibraryService` has no `GetSheetAsync`/`UpdateSheetPageCountAsync` members, so this won't compile until Step 3.

- [ ] **Step 3: Implement**

Add to `src/PiccoloReader.Core/Services/LibraryService.cs`, alongside the other methods:

```csharp
public Task<Sheet> GetSheetAsync(int sheetId) =>
    _database.Connection.Table<Sheet>().Where(s => s.Id == sheetId).FirstAsync();

public Task UpdateSheetPageCountAsync(Sheet sheet, int pageCount)
{
    sheet.PageCount = pageCount;
    return _database.Connection.UpdateAsync(sheet);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
Expected: all tests pass, including the two new ones (total count goes from 20 to 22).

- [ ] **Step 5: Commit**

```bash
git add src/PiccoloReader.Core/Services/LibraryService.cs tests/PiccoloReader.Core.Tests/Services/LibraryServiceTests.cs
git commit -m "feat: add LibraryService.GetSheetAsync and UpdateSheetPageCountAsync"
```

---

### Task 3: SheetViewerViewModel

**Files:**
- Create: `src/PiccoloReader.Core/ViewModels/SheetViewerViewModel.cs`
- Create: `tests/PiccoloReader.Core.Tests/FakePdfPageRenderer.cs`
- Test: `tests/PiccoloReader.Core.Tests/ViewModels/SheetViewerViewModelTests.cs`

**Interfaces:**
- Consumes:
  - `IPdfPageRenderer.GetPageCountAsync(string filePath)` / `.RenderPageAsync(string filePath, int pageIndex, int targetWidthPx, int targetHeightPx)` (Task 1)
  - `LibraryService.GetSheetAsync(int sheetId)` / `.UpdateSheetPageCountAsync(Sheet sheet, int pageCount)` (Task 2)
  - `IAppStorageProvider.SheetsDirectory` (existing)
- Produces (consumed by `SheetViewerPage` in Task 4):
  - `SheetViewerViewModel(LibraryService libraryService, IAppStorageProvider storageProvider, IPdfPageRenderer pdfPageRenderer)`
  - `Task LoadAsync(int sheetId, int targetWidthPx, int targetHeightPx)`
  - `string Title { get; }`, `int PageCount { get; }`, `int CurrentPageIndex { get; }` (0-based), `int CurrentPageDisplay { get; }` (1-based, `CurrentPageIndex + 1`), `string PageIndicatorText { get; }` (`"Page {CurrentPageDisplay} of {PageCount}"`), `byte[]? CurrentPageImageBytes { get; }`, `bool IsLoading { get; }`
  - `IAsyncRelayCommand NextPageCommand`, `IAsyncRelayCommand PreviousPageCommand` (generated by `[RelayCommand]` from the private `NextPageAsync`/`PreviousPageAsync` methods below — CommunityToolkit.Mvvm names the generated command `<MethodName minus Async>Command`)

First, create the test double other tests in the suite can reuse:

```csharp
// tests/PiccoloReader.Core.Tests/FakePdfPageRenderer.cs
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests;

public class FakePdfPageRenderer : IPdfPageRenderer
{
    public int PageCountToReturn { get; set; } = 3;

    public int GetPageCountCallCount { get; private set; }

    public List<int> RenderedPageIndexes { get; } = new();

    public Task<int> GetPageCountAsync(string filePath)
    {
        GetPageCountCallCount++;
        return Task.FromResult(PageCountToReturn);
    }

    public Task<byte[]> RenderPageAsync(string filePath, int pageIndex, int targetWidthPx, int targetHeightPx)
    {
        RenderedPageIndexes.Add(pageIndex);
        return Task.FromResult(new byte[] { (byte)pageIndex });
    }
}
```

- [ ] **Step 1: Write the failing tests**

The fixture inserts sheets directly through `AppDatabase.Connection` (same pattern `LibraryServiceTests`'s own `InsertSheetAsync` helper uses) rather than through `PdfImportService`, since that needs a real file on disk and this ViewModel only cares about `Sheet` rows and a fake renderer:

```csharp
// tests/PiccoloReader.Core.Tests/ViewModels/SheetViewerViewModelTests.cs
using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.ViewModels;

public class SheetViewerViewModelTests : IDisposable
{
    private readonly TestAppStorageProvider _storage = new();
    private readonly AppDatabase _database;
    private readonly LibraryService _libraryService;
    private readonly FakePdfPageRenderer _renderer = new();
    private readonly SheetViewerViewModel _sut;

    public SheetViewerViewModelTests()
    {
        Batteries_V2.Init();
        _database = new AppDatabase(_storage.DatabasePath);
        _database.InitializeAsync().GetAwaiter().GetResult();
        _libraryService = new LibraryService(_database, _storage);
        _sut = new SheetViewerViewModel(_libraryService, _storage, _renderer);
    }

    public void Dispose() => _storage.Dispose();

    private async Task<Sheet> InsertSheetAsync(int pageCount)
    {
        var sheet = new Sheet
        {
            FolderId = null,
            Title = "My Piece",
            FileName = "irrelevant.pdf",
            PageCount = pageCount,
            DateAdded = DateTime.UtcNow
        };
        await _database.Connection.InsertAsync(sheet);
        return sheet;
    }

    [Fact]
    public async Task LoadAsync_SetsTitleAndPageCountFromSheet()
    {
        var sheet = await InsertSheetAsync(pageCount: 5);

        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        Assert.Equal("My Piece", _sut.Title);
        Assert.Equal(5, _sut.PageCount);
        Assert.Equal(0, _sut.CurrentPageIndex);
        Assert.Equal(1, _sut.CurrentPageDisplay);
    }

    [Fact]
    public async Task LoadAsync_PageCountZero_ComputesAndPersistsFromRenderer()
    {
        _renderer.PageCountToReturn = 7;
        var sheet = await InsertSheetAsync(pageCount: 0);

        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        Assert.Equal(7, _sut.PageCount);
        Assert.Equal(1, _renderer.GetPageCountCallCount);

        var reloaded = await _libraryService.GetSheetAsync(sheet.Id);
        Assert.Equal(7, reloaded.PageCount);
    }

    [Fact]
    public async Task LoadAsync_PageCountAlreadyKnown_DoesNotCallRendererForPageCount()
    {
        var sheet = await InsertSheetAsync(pageCount: 5);

        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        Assert.Equal(0, _renderer.GetPageCountCallCount);
    }

    [Fact]
    public async Task LoadAsync_RendersFirstPage()
    {
        var sheet = await InsertSheetAsync(pageCount: 5);

        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        Assert.Equal(new byte[] { 0 }, _sut.CurrentPageImageBytes);
        Assert.Equal(new List<int> { 0 }, _renderer.RenderedPageIndexes);
    }

    [Fact]
    public async Task NextPageAsync_AdvancesPageAndRendersIt()
    {
        var sheet = await InsertSheetAsync(pageCount: 5);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        await _sut.NextPageCommand.ExecuteAsync(null);

        Assert.Equal(1, _sut.CurrentPageIndex);
        Assert.Equal(2, _sut.CurrentPageDisplay);
        Assert.Equal(new byte[] { 1 }, _sut.CurrentPageImageBytes);
    }

    [Fact]
    public async Task NextPageCommand_AtLastPage_CannotExecute()
    {
        var sheet = await InsertSheetAsync(pageCount: 2);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);
        await _sut.NextPageCommand.ExecuteAsync(null);

        Assert.False(_sut.NextPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task PreviousPageCommand_AtFirstPage_CannotExecute()
    {
        var sheet = await InsertSheetAsync(pageCount: 5);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);

        Assert.False(_sut.PreviousPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task PreviousPageAsync_AfterNext_GoesBackToFirstPage()
    {
        var sheet = await InsertSheetAsync(pageCount: 5);
        await _sut.LoadAsync(sheet.Id, targetWidthPx: 800, targetHeightPx: 1000);
        await _sut.NextPageCommand.ExecuteAsync(null);

        await _sut.PreviousPageCommand.ExecuteAsync(null);

        Assert.Equal(0, _sut.CurrentPageIndex);
        Assert.True(_sut.PreviousPageCommand.CanExecute(null) == false);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter "FullyQualifiedName~SheetViewerViewModelTests"`
Expected: FAIL to compile — `SheetViewerViewModel` doesn't exist yet.

- [ ] **Step 3: Implement SheetViewerViewModel**

```csharp
// src/PiccoloReader.Core/ViewModels/SheetViewerViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.ViewModels;

public partial class SheetViewerViewModel : ObservableObject
{
    private readonly LibraryService _libraryService;
    private readonly IAppStorageProvider _storageProvider;
    private readonly IPdfPageRenderer _pdfPageRenderer;

    private string _filePath = string.Empty;
    private int _targetWidthPx;
    private int _targetHeightPx;

    public SheetViewerViewModel(LibraryService libraryService, IAppStorageProvider storageProvider, IPdfPageRenderer pdfPageRenderer)
    {
        _libraryService = libraryService;
        _storageProvider = storageProvider;
        _pdfPageRenderer = pdfPageRenderer;
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
            Title = sheet.Title;
            _filePath = Path.Combine(_storageProvider.SheetsDirectory, sheet.FileName);

            var pageCount = sheet.PageCount;
            if (pageCount <= 0)
            {
                pageCount = await _pdfPageRenderer.GetPageCountAsync(_filePath);
                await _libraryService.UpdateSheetPageCountAsync(sheet, pageCount);
            }

            PageCount = pageCount;
            CurrentPageIndex = 0;

            await LoadCurrentPageAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanGoToNextPage() => CurrentPageIndex < PageCount - 1;

    private bool CanGoToPreviousPage() => CurrentPageIndex > 0;

    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    private async Task NextPageAsync()
    {
        CurrentPageIndex++;
        await LoadCurrentPageAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPage))]
    private async Task PreviousPageAsync()
    {
        CurrentPageIndex--;
        await LoadCurrentPageAsync();
    }

    private async Task LoadCurrentPageAsync()
    {
        CurrentPageImageBytes = await _pdfPageRenderer.RenderPageAsync(_filePath, CurrentPageIndex, _targetWidthPx, _targetHeightPx);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
Expected: all tests pass (total count goes from 22 to 30).

- [ ] **Step 5: Commit**

```bash
git add src/PiccoloReader.Core/ViewModels/SheetViewerViewModel.cs tests/PiccoloReader.Core.Tests/FakePdfPageRenderer.cs tests/PiccoloReader.Core.Tests/ViewModels/SheetViewerViewModelTests.cs
git commit -m "feat: add SheetViewerViewModel with page navigation"
```

---

### Task 4: SheetViewerPage — display, zoom/pan, tap/swipe to turn pages

**Files:**
- Create: `src/PiccoloReader/Views/SheetViewerPage.xaml`
- Create: `src/PiccoloReader/Views/SheetViewerPage.xaml.cs`
- Create: `src/PiccoloReader/Converters/ByteArrayToImageSourceConverter.cs`
- Modify: `src/PiccoloReader/MauiProgram.cs` (register `SheetViewerViewModel` and `SheetViewerPage` for DI)

**Interfaces:**
- Consumes: `SheetViewerViewModel` (Task 3) — constructor-injects it like `LibraryPage`/`FolderPage` already do with their view models; calls `LoadAsync(sheetId, targetWidthPx, targetHeightPx)` from `OnAppearing`.
- Produces: `SheetViewerPage(SheetViewerViewModel viewModel)` constructor, `[QueryProperty(nameof(SheetId), "sheetId")] public string SheetId { set; }` — consumed by Task 5's routing/navigation code.

No automated tests for this task — it's a MAUI `ContentPage` with gesture handling, verified manually (same reasoning as Task 1: UI/platform code, not unit-testable business logic). Manual verification happens in Task 5 once there's a way to navigate to this page.

- [ ] **Step 1: Write the byte[]-to-ImageSource converter**

```csharp
// src/PiccoloReader/Converters/ByteArrayToImageSourceConverter.cs
using System.Globalization;

namespace PiccoloReader.Converters;

public class ByteArrayToImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is byte[] bytes && bytes.Length > 0)
        {
            return ImageSource.FromStream(() => new MemoryStream(bytes));
        }

        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

- [ ] **Step 2: Write SheetViewerPage.xaml**

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ContentPage
    x:Class="PiccoloReader.Views.SheetViewerPage"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:converters="clr-namespace:PiccoloReader.Converters"
    Title="{Binding Title}">

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
                <SwipeGestureRecognizer Direction="Left" Swiped="OnSwipedLeft" />
                <SwipeGestureRecognizer Direction="Right" Swiped="OnSwipedRight" />
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

    </Grid>
</ContentPage>
```

- [ ] **Step 3: Write SheetViewerPage.xaml.cs**

```csharp
// src/PiccoloReader/Views/SheetViewerPage.xaml.cs
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Views;

[QueryProperty(nameof(SheetId), "sheetId")]
public partial class SheetViewerPage : ContentPage
{
    private readonly SheetViewerViewModel _viewModel;

    private double _currentScale = 1;
    private double _startScale = 1;
    private double _xOffset;
    private double _yOffset;

    public SheetViewerPage(SheetViewerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    public string SheetId { get; set; } = string.Empty;

    protected override async void OnAppearing()
    {
        base.OnAppearing();

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

    private void ResetZoom()
    {
        _currentScale = 1;
        _startScale = 1;
        _xOffset = 0;
        _yOffset = 0;
        PageImage.Scale = 1;
        PageImage.TranslationX = 0;
        PageImage.TranslationY = 0;
    }

    private void OnPageTapped(object? sender, TappedEventArgs e)
    {
        if (_currentScale > 1.05)
        {
            // Ignore tap-to-turn while zoomed in - the user is most likely
            // trying to look around the page, not turn it.
            return;
        }

        var position = e.GetPosition(PageImage);
        if (position is null)
        {
            return;
        }

        if (position.Value.X < PageImage.Width / 2)
        {
            if (_viewModel.PreviousPageCommand.CanExecute(null))
            {
                _viewModel.PreviousPageCommand.Execute(null);
                ResetZoom();
            }
        }
        else
        {
            if (_viewModel.NextPageCommand.CanExecute(null))
            {
                _viewModel.NextPageCommand.Execute(null);
                ResetZoom();
            }
        }
    }

    private void OnSwipedLeft(object? sender, SwipedEventArgs e)
    {
        if (_currentScale > 1.05)
        {
            return;
        }

        if (_viewModel.NextPageCommand.CanExecute(null))
        {
            _viewModel.NextPageCommand.Execute(null);
            ResetZoom();
        }
    }

    private void OnSwipedRight(object? sender, SwipedEventArgs e)
    {
        if (_currentScale > 1.05)
        {
            return;
        }

        if (_viewModel.PreviousPageCommand.CanExecute(null))
        {
            _viewModel.PreviousPageCommand.Execute(null);
            ResetZoom();
        }
    }

    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status == GestureStatus.Started)
        {
            _startScale = PageImage.Scale;
            PageImage.AnchorX = 0;
            PageImage.AnchorY = 0;
        }

        if (e.Status == GestureStatus.Running)
        {
            _currentScale = Math.Max(1, _startScale * e.Scale);

            var renderedX = PageImage.X + _xOffset;
            var deltaX = renderedX / PageImage.Width;
            var deltaWidth = PageImage.Width / (PageImage.Width * _startScale);
            var originX = (e.ScaleOrigin.X - deltaX) * deltaWidth;

            var renderedY = PageImage.Y + _yOffset;
            var deltaY = renderedY / PageImage.Height;
            var deltaHeight = PageImage.Height / (PageImage.Height * _startScale);
            var originY = (e.ScaleOrigin.Y - deltaY) * deltaHeight;

            var targetX = _xOffset - (originX * PageImage.Width * (_currentScale - _startScale));
            var targetY = _yOffset - (originY * PageImage.Height * (_currentScale - _startScale));

            PageImage.TranslationX = targetX;
            PageImage.TranslationY = targetY;
            PageImage.Scale = _currentScale;
        }

        if (e.Status == GestureStatus.Completed)
        {
            _xOffset = PageImage.TranslationX;
            _yOffset = PageImage.TranslationY;
        }
    }

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (_currentScale <= 1.05)
        {
            // Not zoomed in - don't pan (also avoids fighting the swipe-to-turn gestures).
            return;
        }

        switch (e.StatusType)
        {
            case GestureStatus.Running:
                PageImage.TranslationX = _xOffset + e.TotalX;
                PageImage.TranslationY = _yOffset + e.TotalY;
                break;

            case GestureStatus.Completed:
                _xOffset = PageImage.TranslationX;
                _yOffset = PageImage.TranslationY;
                break;
        }
    }
}
```

- [ ] **Step 4: Register in MauiProgram.cs**

Add near the other page/view-model registrations in `src/PiccoloReader/MauiProgram.cs`:

```csharp
builder.Services.AddTransient<SheetViewerViewModel>();
builder.Services.AddTransient<SheetViewerPage>();
```

`SheetViewerViewModel` is in `PiccoloReader.Core.ViewModels` (already `using`d) and `SheetViewerPage` is in `PiccoloReader.Views` (already `using`d).

- [ ] **Step 5: Build for Android to verify it compiles**

Run: `dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add src/PiccoloReader/Views/SheetViewerPage.xaml src/PiccoloReader/Views/SheetViewerPage.xaml.cs src/PiccoloReader/Converters/ByteArrayToImageSourceConverter.cs src/PiccoloReader/MauiProgram.cs
git commit -m "feat: add SheetViewerPage with zoom/pan and tap/swipe page turning"
```

---

### Task 5: Route registration, tap-to-open wiring, end-to-end verification

**Files:**
- Modify: `src/PiccoloReader/AppShell.xaml.cs` (register `sheetviewer` route)
- Modify: `src/PiccoloReader/Views/LibraryPage.xaml` (add tap gesture to the Sheets `DataTemplate`)
- Modify: `src/PiccoloReader/Views/LibraryPage.xaml.cs` (add `OnSheetTapped` handler)
- Modify: `src/PiccoloReader/Views/FolderPage.xaml` (add tap gesture to the Sheets `DataTemplate`)
- Modify: `src/PiccoloReader/Views/FolderPage.xaml.cs` (add `OnSheetTapped` handler)

**Interfaces:**
- Consumes: `SheetViewerPage` (Task 4), registered as Shell route `"sheetviewer"`, reads `sheetId` query property.
- Produces: nothing further — this is the last task in the plan.

No new automated tests — this is navigation wiring, verified manually end-to-end (real PDF import → open → page turn → zoom/pan) on the Android emulator, which is also the first point where Task 1's real Android `PdfPageRenderer` actually runs.

- [ ] **Step 1: Register the route**

In `src/PiccoloReader/AppShell.xaml.cs`, add alongside the existing `"folder"` registration:

```csharp
Routing.RegisterRoute("sheetviewer", typeof(Views.SheetViewerPage));
```

- [ ] **Step 2: Wire tap-to-open in LibraryPage**

In `src/PiccoloReader/Views/LibraryPage.xaml`, the Sheets `CollectionView`'s `DataTemplate` (`Border x:Name="SheetItemRoot"`) currently only has `Border.Behaviors` for long-press. Add a `Border.GestureRecognizers` block, mirroring the Folders template just above it:

```xml
<Border.GestureRecognizers>
    <TapGestureRecognizer Tapped="OnSheetTapped" CommandParameter="{Binding}" />
</Border.GestureRecognizers>
```

Place it right after the opening `<Border ...>` tag, before `<Border.Behaviors>`.

In `src/PiccoloReader/Views/LibraryPage.xaml.cs`, add a handler alongside `OnFolderTapped`:

```csharp
private async void OnSheetTapped(object? sender, TappedEventArgs e)
{
    if (e.Parameter is Sheet sheet)
    {
        await Shell.Current.GoToAsync($"sheetviewer?sheetId={sheet.Id}");
    }
}
```

- [ ] **Step 3: Wire tap-to-open in FolderPage**

Same change in `src/PiccoloReader/Views/FolderPage.xaml` (the Sheets template's `Border x:Name="SheetItemRoot"`) and `src/PiccoloReader/Views/FolderPage.xaml.cs` — identical `OnSheetTapped` handler.

- [ ] **Step 4: Build and run on the Android emulator**

Run: `dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android -t:Run`
Expected: `Build succeeded`, app launches to Library.

- [ ] **Step 5: Manual verification checklist**

On the running emulator:
1. Import a real PDF (multi-page, if available) via the Import toolbar icon.
2. Tap the new sheet card — it should navigate to the Sheet Viewer and show page 1, with a "Page 1 of N" indicator at the bottom.
3. Tap the right half of the page — advances to page 2. Tap the left half — goes back to page 1.
4. Swipe left — advances a page. Swipe right — goes back.
5. On the last page, confirm tapping/swiping right does nothing (no crash, no out-of-range).
6. Pinch to zoom in — the page scales up smoothly. Drag while zoomed — pans around. Pinch back out.
7. Navigate back (Shell back button) to the Folder/Library page — no crash.
8. Repeat the "tap sheet to open" step for a sheet inside a folder (Folder page), not just root.

Capture a screenshot of the Sheet Viewer mid-zoom and one at normal zoom, confirm via `adb logcat` that there's no `FATAL`/`AndroidRuntime` crash during the whole flow (same verification pattern used throughout this project).

- [ ] **Step 6: Run the full Core test suite one more time**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
Expected: all 30 tests pass (unchanged from Task 3 — this task touches no Core code).

- [ ] **Step 7: Commit**

```bash
git add src/PiccoloReader/AppShell.xaml.cs src/PiccoloReader/Views/LibraryPage.xaml src/PiccoloReader/Views/LibraryPage.xaml.cs src/PiccoloReader/Views/FolderPage.xaml src/PiccoloReader/Views/FolderPage.xaml.cs
git commit -m "feat: wire sheet tap navigation to Sheet Viewer"
```

- [ ] **Step 8: Update the roadmap**

In `docs/superpowers/plans/roadmap.md`, replace the "Plan 2: PDF Viewer" section's "Not planned yet" line with a task table (same shape as Plan 1's), and fill in PR links as each task's PR is opened/merged during execution.

---

## Post-Plan Notes

- **iOS renderer is still stubbed.** When there's a way to build/test iOS (a Mac, a CI runner with Xcode), replace `src/PiccoloReader/Platforms/iOS/PdfPageRenderer.cs`'s `NotImplementedException` bodies with a real `PDFKit`-based implementation. No other code needs to change — `SheetViewerViewModel` and `SheetViewerPage` are already platform-agnostic.
- **Annotation overlay is not part of this plan.** The Sheet Viewer currently shows only the rasterized page — no pencil-icon toolbar button, no tool panel, no SkiaSharp overlay canvas. That's Plans 3 (icons) and 4 (pencil/eraser), which will add a transparent canvas layer on top of the same `Image` this plan renders, per the architecture spec's two-layer design.
- **Re-rendering on zoom is intentionally not implemented.** Per the spec: "if a user zooms in far past the rendered resolution, the bitmap softens slightly — acceptable for v1, improvable later... without touching the architecture." This plan renders once at screen resolution and zooms the bitmap itself (no re-rasterization).
