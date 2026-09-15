# Project Scaffolding & Library Management — Implementation Plan (Plan 1 of 6)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the PiccoloReader solution (MAUI app + testable Core
library + test project + CI), and deliver a fully working local library
manager: create folders, import PDFs into app storage (at root or in a
folder), move sheets between folders, delete sheets, and delete folders
(with a prompt to keep or delete their sheets). No PDF viewing or
annotation yet — those are later plans.

**Architecture:** Three projects. `PiccoloReader.Core` is a plain
`net10.0` class library holding everything platform-agnostic and unit
testable: SQLite data access, services, and ViewModels (using
`CommunityToolkit.Mvvm`). `PiccoloReader` is the MAUI app head
(`net10.0-android` / `net10.0-ios` only), holding XAML pages and the one
piece of platform glue this plan needs (`IAppStorageProvider`, backed by
MAUI's `FileSystem.AppDataDirectory`). `PiccoloReader.Core.Tests` is an
xUnit project referencing only `Core`, so it runs anywhere without an
Android/iOS workload or emulator — including in CI.

**Tech Stack:** .NET 10 (SDK 10.0.401 confirmed installed, with android /
ios / maccatalyst / maui-windows workloads present), .NET MAUI,
`sqlite-net-pcl` + `SQLitePCLRaw.bundle_e_sqlite3`, `CommunityToolkit.Mvvm`,
xUnit, GitHub Actions.

**Spec:** [`docs/superpowers/specs/2026-09-14-architecture-design.md`](../specs/2026-09-14-architecture-design.md)
(also see [`docs/requirements.md`](../../requirements.md))

## Global Constraints

- Target platforms: **Android and iOS/iPadOS only** (per spec — no
  Windows/macOS TFMs, even though the `dotnet new maui` template adds
  them by default).
- No login/backend — everything is local (per spec).
- Folder structure is flat: root can hold folders and/or sheets; folders
  cannot contain folders (per spec).
- Sheet position/size fields for annotations are out of scope for this
  plan (no `Annotation` table yet) — this plan only needs `Folder` and
  `Sheet` (per spec's data model; annotation table arrives with the
  annotation plans).
- All position values elsewhere in the spec are normalized 0.0–1.0; not
  relevant to this plan's tables but keep this in mind for later plans.
- `Sheet.PageCount` is set to `0` at import time in this plan — no PDF
  parsing dependency is introduced yet. Plan 2 (PDF viewer) populates the
  real page count once native rasterization exists and can report it.
- Branch convention (per `CONTRIBUTING.md`): no direct commits to `main`
  except already-completed initial scaffolding; this plan's work happens
  on a feature branch merged via PR.

---

## Task 1: Solution scaffolding + CI

**Files:**
- Create: `PiccoloReader.sln`
- Create: `src/PiccoloReader/PiccoloReader.csproj` (+ generated MAUI
  template files: `MauiProgram.cs`, `App.xaml`, `App.xaml.cs`,
  `AppShell.xaml`, `AppShell.xaml.cs`, `MainPage.xaml`,
  `MainPage.xaml.cs`, `Properties/launchSettings.json`, `Resources/**`)
- Create: `src/PiccoloReader.Core/PiccoloReader.Core.csproj`
- Create: `tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
- Create: `tests/PiccoloReader.Core.Tests/SmokeTests.cs`
- Create: `.github/workflows/ci.yml`
- Modify: `.gitignore` (add standard .NET/MAUI ignores)

**Interfaces:**
- Produces: solution `PiccoloReader.sln` referencing all three projects;
  `PiccoloReader.Core.csproj` with a project reference from both
  `PiccoloReader.csproj` and `PiccoloReader.Core.Tests.csproj`.

- [ ] **Step 1: Create the branch**

```bash
git checkout main
git pull origin main
git checkout -b feature/project-scaffolding
```

- [ ] **Step 2: Scaffold the MAUI app project**

```bash
dotnet new maui -n PiccoloReader -o src/PiccoloReader
```

- [ ] **Step 3: Restrict the MAUI app to Android + iOS only**

Open `src/PiccoloReader/PiccoloReader.csproj`. Replace the
`<TargetFrameworks>` block (three conditioned lines for
android/ios+maccatalyst/windows) with a single fixed line, and remove the
now-irrelevant Windows packaging/versioning properties:

```xml
<TargetFrameworks>net10.0-android;net10.0-ios</TargetFrameworks>
```

Remove these lines entirely (they only apply to TFMs we no longer target):

```xml
<WindowsPackageType>None</WindowsPackageType>
```
```xml
<SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'maccatalyst'">15.0</SupportedOSPlatformVersion>
<SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'windows'">10.0.17763.0</SupportedOSPlatformVersion>
<TargetPlatformMinVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'windows'">10.0.17763.0</TargetPlatformMinVersion>
```

Also update the application id from the template default to a real one:

```xml
<ApplicationId>com.fsoftt.piccoloreader</ApplicationId>
```

- [ ] **Step 4: Scaffold the Core class library**

```bash
dotnet new classlib -n PiccoloReader.Core -o src/PiccoloReader.Core -f net10.0
rm src/PiccoloReader.Core/Class1.cs
```

Edit `src/PiccoloReader.Core/PiccoloReader.Core.csproj` so it reads:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```

- [ ] **Step 5: Scaffold the test project**

```bash
dotnet new xunit -n PiccoloReader.Core.Tests -o tests/PiccoloReader.Core.Tests -f net10.0
rm tests/PiccoloReader.Core.Tests/UnitTest1.cs
```

- [ ] **Step 6: Wire up the solution and project references**

```bash
dotnet new sln -n PiccoloReader
dotnet sln PiccoloReader.sln add src/PiccoloReader/PiccoloReader.csproj
dotnet sln PiccoloReader.sln add src/PiccoloReader.Core/PiccoloReader.Core.csproj
dotnet sln PiccoloReader.sln add tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj
dotnet add src/PiccoloReader/PiccoloReader.csproj reference src/PiccoloReader.Core/PiccoloReader.Core.csproj
dotnet add tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj reference src/PiccoloReader.Core/PiccoloReader.Core.csproj
```

- [ ] **Step 7: Add a smoke test**

Create `tests/PiccoloReader.Core.Tests/SmokeTests.cs`:

```csharp
namespace PiccoloReader.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void TestProject_IsWiredUpCorrectly()
    {
        Assert.True(true);
    }
}
```

(This is removed in Task 2 once real tests exist — it only exists to
prove the solution/test/CI wiring works end to end.)

- [ ] **Step 8: Verify the test project builds and runs**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
Expected: PASS (1 test)

- [ ] **Step 9: Verify the full solution builds**

Run: `dotnet build PiccoloReader.sln`
Expected: builds successfully for `net10.0-android` and `net10.0-ios`
targets (both workloads are already installed).

- [ ] **Step 10: Update `.gitignore`**

Add to the existing `.gitignore`:

```
## .NET / MAUI
bin/
obj/
.vs/
*.user
```

- [ ] **Step 11: Add the CI workflow**

Create `.github/workflows/ci.yml`. Scoped to the unit test project only —
building the full MAUI app needs Android/iOS workloads on the runner,
which is unnecessary just to run unit tests and would make CI much
slower and more fragile:

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj

      - name: Test
        run: dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --no-restore --verbosity normal
```

- [ ] **Step 12: Commit and push**

```bash
git add PiccoloReader.sln src/ tests/ .github/ .gitignore
git commit -m "chore: scaffold solution, Core lib, test project, and CI"
git push -u origin feature/project-scaffolding
```

- [ ] **Step 13: Open a PR and verify CI passes**

```bash
gh pr create --title "Scaffold solution, Core lib, test project, and CI" --body "Sets up PiccoloReader.sln (MAUI app + Core lib + xUnit test project) and a GitHub Actions workflow that runs the unit tests on push/PR to main."
gh pr checks --watch
```

Expected: the `test` job passes. Do not merge yet — later tasks in this
plan continue on the same branch.

---

## Task 2: SQLite data layer (Folder, Sheet, AppDatabase)

**Files:**
- Create: `src/PiccoloReader.Core/Data/Models/Folder.cs`
- Create: `src/PiccoloReader.Core/Data/Models/Sheet.cs`
- Create: `src/PiccoloReader.Core/Data/AppDatabase.cs`
- Create: `src/PiccoloReader.Core/Services/IAppStorageProvider.cs`
- Create: `tests/PiccoloReader.Core.Tests/Data/AppDatabaseTests.cs`
- Modify: `src/PiccoloReader.Core/PiccoloReader.Core.csproj` (add
  `sqlite-net-pcl` package reference)
- Modify: `tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
  (add `sqlite-net-pcl` and `SQLitePCLRaw.bundle_e_sqlite3` package
  references — the test project needs the native SQLite binaries since
  nothing else supplies them yet)
- Delete: `tests/PiccoloReader.Core.Tests/SmokeTests.cs`

**Interfaces:**
- Produces: `Folder { int Id; string Name; }`,
  `Sheet { int Id; int? FolderId; string Title; string FileName; int
  PageCount; DateTime DateAdded; }`,
  `IAppStorageProvider { string DatabasePath; string SheetsDirectory; }`,
  `AppDatabase(string databasePath)` with `SQLiteAsyncConnection
  Connection { get; }` and `Task InitializeAsync()`.

- [ ] **Step 1: Add the sqlite-net-pcl package to Core**

```bash
dotnet add src/PiccoloReader.Core/PiccoloReader.Core.csproj package sqlite-net-pcl
```

- [ ] **Step 2: Add sqlite-net-pcl + native bundle to the test project**

```bash
dotnet add tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj package sqlite-net-pcl
dotnet add tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj package SQLitePCLRaw.bundle_e_sqlite3
```

- [ ] **Step 3: Write the failing test for AppDatabase**

Create `tests/PiccoloReader.Core.Tests/Data/AppDatabaseTests.cs`:

```csharp
using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.Data;

public class AppDatabaseTests : IDisposable
{
    private readonly string _dbPath;

    public AppDatabaseTests()
    {
        Batteries_V2.Init();
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db3");
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public async Task InitializeAsync_CreatesFolderAndSheetTables()
    {
        var database = new AppDatabase(_dbPath);
        await database.InitializeAsync();

        var folder = new Folder { Name = "Orchestra" };
        await database.Connection.InsertAsync(folder);

        var sheet = new Sheet
        {
            FolderId = folder.Id,
            Title = "Symphony No. 5",
            FileName = "abc123.pdf",
            PageCount = 0,
            DateAdded = DateTime.UtcNow
        };
        await database.Connection.InsertAsync(sheet);

        var folders = await database.Connection.Table<Folder>().ToListAsync();
        var sheets = await database.Connection.Table<Sheet>().ToListAsync();

        Assert.Single(folders);
        Assert.Single(sheets);
        Assert.Equal(folder.Id, sheets[0].FolderId);
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter AppDatabaseTests`
Expected: FAIL (compile error — `Folder`, `Sheet`, `AppDatabase` don't
exist yet)

- [ ] **Step 5: Create the Folder model**

Create `src/PiccoloReader.Core/Data/Models/Folder.cs`:

```csharp
using SQLite;

namespace PiccoloReader.Core.Data.Models;

public class Folder
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
```

- [ ] **Step 6: Create the Sheet model**

Create `src/PiccoloReader.Core/Data/Models/Sheet.cs`:

```csharp
using SQLite;

namespace PiccoloReader.Core.Data.Models;

public class Sheet
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int? FolderId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public int PageCount { get; set; }

    public DateTime DateAdded { get; set; }
}
```

- [ ] **Step 7: Create IAppStorageProvider**

Create `src/PiccoloReader.Core/Services/IAppStorageProvider.cs`:

```csharp
namespace PiccoloReader.Core.Services;

public interface IAppStorageProvider
{
    string DatabasePath { get; }

    string SheetsDirectory { get; }
}
```

- [ ] **Step 8: Create AppDatabase**

Create `src/PiccoloReader.Core/Data/AppDatabase.cs`:

```csharp
using PiccoloReader.Core.Data.Models;
using SQLite;

namespace PiccoloReader.Core.Data;

public class AppDatabase
{
    public AppDatabase(string databasePath)
    {
        Connection = new SQLiteAsyncConnection(databasePath);
    }

    public SQLiteAsyncConnection Connection { get; }

    public async Task InitializeAsync()
    {
        await Connection.CreateTableAsync<Folder>();
        await Connection.CreateTableAsync<Sheet>();
    }
}
```

- [ ] **Step 9: Run test to verify it passes**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter AppDatabaseTests`
Expected: PASS

- [ ] **Step 10: Remove the smoke test**

```bash
rm tests/PiccoloReader.Core.Tests/SmokeTests.cs
```

- [ ] **Step 11: Run the full test suite**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
Expected: PASS (1 test)

- [ ] **Step 12: Commit**

```bash
git add src/PiccoloReader.Core tests/PiccoloReader.Core.Tests
git commit -m "feat: add SQLite data layer (Folder, Sheet, AppDatabase)"
git push
```

---

## Task 3: LibraryService (folder/sheet CRUD)

**Files:**
- Create: `src/PiccoloReader.Core/Services/LibraryService.cs`
- Create: `tests/PiccoloReader.Core.Tests/Services/LibraryServiceTests.cs`
- Create: `tests/PiccoloReader.Core.Tests/TestAppStorageProvider.cs`

**Interfaces:**
- Consumes: `AppDatabase` (Task 2), `IAppStorageProvider` (Task 2)
- Produces: `LibraryService(AppDatabase database, IAppStorageProvider
  storageProvider)` with:
  - `Task<List<Folder>> GetFoldersAsync()`
  - `Task<List<Sheet>> GetSheetsAsync(int? folderId)`
  - `Task<Folder> CreateFolderAsync(string name)`
  - `Task DeleteFolderAsync(int folderId, bool deleteSheets)`
  - `Task MoveSheetAsync(Sheet sheet, int? targetFolderId)`
  - `Task DeleteSheetAsync(Sheet sheet)`

- [ ] **Step 1: Create a shared test storage provider**

Create `tests/PiccoloReader.Core.Tests/TestAppStorageProvider.cs`:

```csharp
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests;

public class TestAppStorageProvider : IAppStorageProvider, IDisposable
{
    private readonly string _rootDirectory;

    public TestAppStorageProvider()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), $"piccolo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootDirectory);
    }

    public string DatabasePath => Path.Combine(_rootDirectory, "test.db3");

    public string SheetsDirectory => Path.Combine(_rootDirectory, "Sheets");

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Write the failing tests for LibraryService**

Create `tests/PiccoloReader.Core.Tests/Services/LibraryServiceTests.cs`:

```csharp
using PiccoloReader.Core.Data;
using PiccoloReader.Core.Services;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.Services;

public class LibraryServiceTests : IDisposable
{
    private readonly TestAppStorageProvider _storage = new();
    private readonly LibraryService _sut;

    public LibraryServiceTests()
    {
        Batteries_V2.Init();
        var database = new AppDatabase(_storage.DatabasePath);
        database.InitializeAsync().GetAwaiter().GetResult();
        _sut = new LibraryService(database, _storage);
    }

    public void Dispose() => _storage.Dispose();

    [Fact]
    public async Task CreateFolderAsync_AddsFolderRetrievableByGetFoldersAsync()
    {
        await _sut.CreateFolderAsync("Orchestra");

        var folders = await _sut.GetFoldersAsync();

        Assert.Single(folders);
        Assert.Equal("Orchestra", folders[0].Name);
    }

    [Fact]
    public async Task GetSheetsAsync_NullFolderId_ReturnsOnlyRootSheets()
    {
        var folder = await _sut.CreateFolderAsync("Orchestra");
        await _sut.MoveSheetAsync(await InsertSheetAsync(folder.Id), folder.Id);
        var rootSheet = await InsertSheetAsync(null);

        var rootSheets = await _sut.GetSheetsAsync(null);

        Assert.Single(rootSheets);
        Assert.Equal(rootSheet.Id, rootSheets[0].Id);
    }

    [Fact]
    public async Task DeleteFolderAsync_WithDeleteSheetsTrue_RemovesFolderAndItsSheets()
    {
        var folder = await _sut.CreateFolderAsync("Orchestra");
        var sheet = await InsertSheetAsync(folder.Id);

        await _sut.DeleteFolderAsync(folder.Id, deleteSheets: true);

        Assert.Empty(await _sut.GetFoldersAsync());
        Assert.Empty(await _sut.GetSheetsAsync(folder.Id));
        Assert.False(File.Exists(Path.Combine(_storage.SheetsDirectory, sheet.FileName)));
    }

    [Fact]
    public async Task DeleteFolderAsync_WithDeleteSheetsFalse_MovesSheetsToRoot()
    {
        var folder = await _sut.CreateFolderAsync("Orchestra");
        var sheet = await InsertSheetAsync(folder.Id);

        await _sut.DeleteFolderAsync(folder.Id, deleteSheets: false);

        var rootSheets = await _sut.GetSheetsAsync(null);
        Assert.Single(rootSheets);
        Assert.Equal(sheet.Id, rootSheets[0].Id);
    }

    [Fact]
    public async Task MoveSheetAsync_UpdatesFolderId()
    {
        var folder = await _sut.CreateFolderAsync("Orchestra");
        var sheet = await InsertSheetAsync(null);

        await _sut.MoveSheetAsync(sheet, folder.Id);

        var sheetsInFolder = await _sut.GetSheetsAsync(folder.Id);
        Assert.Single(sheetsInFolder);
    }

    [Fact]
    public async Task DeleteSheetAsync_RemovesRowAndFile()
    {
        var sheet = await InsertSheetAsync(null);
        var filePath = Path.Combine(_storage.SheetsDirectory, sheet.FileName);

        await _sut.DeleteSheetAsync(sheet);

        Assert.Empty(await _sut.GetSheetsAsync(null));
        Assert.False(File.Exists(filePath));
    }

    private async Task<Data.Models.Sheet> InsertSheetAsync(int? folderId)
    {
        Directory.CreateDirectory(_storage.SheetsDirectory);
        var fileName = $"{Guid.NewGuid():N}.pdf";
        await File.WriteAllTextAsync(Path.Combine(_storage.SheetsDirectory, fileName), "fake-pdf");

        var sheet = new Data.Models.Sheet
        {
            FolderId = folderId,
            Title = "Test Sheet",
            FileName = fileName,
            PageCount = 0,
            DateAdded = DateTime.UtcNow
        };

        var database = new AppDatabase(_storage.DatabasePath);
        await database.Connection.InsertAsync(sheet);
        return sheet;
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter LibraryServiceTests`
Expected: FAIL (compile error — `LibraryService` doesn't exist)

- [ ] **Step 4: Implement LibraryService**

Create `src/PiccoloReader.Core/Services/LibraryService.cs`:

```csharp
using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.Services;

public class LibraryService
{
    private readonly AppDatabase _database;
    private readonly IAppStorageProvider _storageProvider;

    public LibraryService(AppDatabase database, IAppStorageProvider storageProvider)
    {
        _database = database;
        _storageProvider = storageProvider;
    }

    public Task<List<Folder>> GetFoldersAsync() =>
        _database.Connection.Table<Folder>().OrderBy(f => f.Name).ToListAsync();

    public Task<List<Sheet>> GetSheetsAsync(int? folderId) =>
        _database.Connection.Table<Sheet>()
            .Where(s => s.FolderId == folderId)
            .OrderBy(s => s.Title)
            .ToListAsync();

    public async Task<Folder> CreateFolderAsync(string name)
    {
        var folder = new Folder { Name = name };
        await _database.Connection.InsertAsync(folder);
        return folder;
    }

    public async Task DeleteFolderAsync(int folderId, bool deleteSheets)
    {
        var sheets = await GetSheetsAsync(folderId);

        foreach (var sheet in sheets)
        {
            if (deleteSheets)
            {
                await DeleteSheetAsync(sheet);
            }
            else
            {
                sheet.FolderId = null;
                await _database.Connection.UpdateAsync(sheet);
            }
        }

        var folder = await _database.Connection.Table<Folder>()
            .Where(f => f.Id == folderId)
            .FirstAsync();
        await _database.Connection.DeleteAsync(folder);
    }

    public Task MoveSheetAsync(Sheet sheet, int? targetFolderId)
    {
        sheet.FolderId = targetFolderId;
        return _database.Connection.UpdateAsync(sheet);
    }

    public async Task DeleteSheetAsync(Sheet sheet)
    {
        await _database.Connection.DeleteAsync(sheet);

        var filePath = Path.Combine(_storageProvider.SheetsDirectory, sheet.FileName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter LibraryServiceTests`
Expected: PASS (6 tests)

- [ ] **Step 6: Commit**

```bash
git add src/PiccoloReader.Core/Services/LibraryService.cs tests/PiccoloReader.Core.Tests
git commit -m "feat: add LibraryService for folder/sheet CRUD"
git push
```

---

## Task 4: PdfImportService

**Files:**
- Create: `src/PiccoloReader.Core/Services/PdfImportService.cs`
- Create: `tests/PiccoloReader.Core.Tests/Services/PdfImportServiceTests.cs`

**Interfaces:**
- Consumes: `AppDatabase`, `IAppStorageProvider` (Task 2)
- Produces: `PdfImportService(AppDatabase database, IAppStorageProvider
  storageProvider)` with `Task<Sheet> ImportAsync(string
  sourceFilePath, int? folderId)`

- [ ] **Step 1: Write the failing tests**

Create `tests/PiccoloReader.Core.Tests/Services/PdfImportServiceTests.cs`:

```csharp
using PiccoloReader.Core.Data;
using PiccoloReader.Core.Services;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.Services;

public class PdfImportServiceTests : IDisposable
{
    private readonly TestAppStorageProvider _storage = new();
    private readonly PdfImportService _sut;
    private readonly string _sourceFilePath;

    public PdfImportServiceTests()
    {
        Batteries_V2.Init();
        var database = new AppDatabase(_storage.DatabasePath);
        database.InitializeAsync().GetAwaiter().GetResult();
        _sut = new PdfImportService(database, _storage);

        _sourceFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-My Piece.pdf");
        File.WriteAllText(_sourceFilePath, "fake-pdf-content");
    }

    public void Dispose()
    {
        _storage.Dispose();
        if (File.Exists(_sourceFilePath))
        {
            File.Delete(_sourceFilePath);
        }
    }

    [Fact]
    public async Task ImportAsync_CopiesFileIntoSheetsDirectory()
    {
        var sheet = await _sut.ImportAsync(_sourceFilePath, folderId: null);

        var copiedPath = Path.Combine(_storage.SheetsDirectory, sheet.FileName);
        Assert.True(File.Exists(copiedPath));
        Assert.NotEqual(_sourceFilePath, copiedPath);
    }

    [Fact]
    public async Task ImportAsync_UsesSourceFileNameAsTitle()
    {
        var sheet = await _sut.ImportAsync(_sourceFilePath, folderId: null);

        Assert.EndsWith("My Piece", sheet.Title);
    }

    [Fact]
    public async Task ImportAsync_SetsFolderIdAndDefaultPageCount()
    {
        var sheet = await _sut.ImportAsync(_sourceFilePath, folderId: 7);

        Assert.Equal(7, sheet.FolderId);
        Assert.Equal(0, sheet.PageCount);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter PdfImportServiceTests`
Expected: FAIL (compile error — `PdfImportService` doesn't exist)

- [ ] **Step 3: Implement PdfImportService**

Create `src/PiccoloReader.Core/Services/PdfImportService.cs`:

```csharp
using PiccoloReader.Core.Data;
using PiccoloReader.Core.Data.Models;

namespace PiccoloReader.Core.Services;

public class PdfImportService
{
    private readonly AppDatabase _database;
    private readonly IAppStorageProvider _storageProvider;

    public PdfImportService(AppDatabase database, IAppStorageProvider storageProvider)
    {
        _database = database;
        _storageProvider = storageProvider;
    }

    public async Task<Sheet> ImportAsync(string sourceFilePath, int? folderId)
    {
        Directory.CreateDirectory(_storageProvider.SheetsDirectory);

        var fileName = $"{Guid.NewGuid():N}.pdf";
        var destinationPath = Path.Combine(_storageProvider.SheetsDirectory, fileName);
        File.Copy(sourceFilePath, destinationPath);

        var sheet = new Sheet
        {
            FolderId = folderId,
            Title = Path.GetFileNameWithoutExtension(sourceFilePath),
            FileName = fileName,
            PageCount = 0,
            DateAdded = DateTime.UtcNow
        };

        await _database.Connection.InsertAsync(sheet);
        return sheet;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter PdfImportServiceTests`
Expected: PASS (3 tests)

- [ ] **Step 5: Commit**

```bash
git add src/PiccoloReader.Core/Services/PdfImportService.cs tests/PiccoloReader.Core.Tests
git commit -m "feat: add PdfImportService"
git push
```

---

## Task 5: LibraryViewModel and FolderViewModel

**Files:**
- Create: `src/PiccoloReader.Core/ViewModels/LibraryViewModel.cs`
- Create: `src/PiccoloReader.Core/ViewModels/FolderViewModel.cs`
- Create: `tests/PiccoloReader.Core.Tests/ViewModels/LibraryViewModelTests.cs`
- Create: `tests/PiccoloReader.Core.Tests/ViewModels/FolderViewModelTests.cs`
- Modify: `src/PiccoloReader.Core/PiccoloReader.Core.csproj` (add
  `CommunityToolkit.Mvvm`)

**Interfaces:**
- Consumes: `LibraryService`, `PdfImportService` (Tasks 3–4)
- Produces:
  - `LibraryViewModel(LibraryService libraryService, PdfImportService
    importService)`:
    `ObservableCollection<Folder> Folders`,
    `ObservableCollection<Sheet> RootSheets`,
    `string NewFolderName` (bindable),
    `Task LoadAsync()`,
    `IAsyncRelayCommand CreateFolderCommand`,
    `IAsyncRelayCommand<string> ImportPdfCommand` (param: source file path),
    `IAsyncRelayCommand<Sheet> DeleteSheetCommand`,
    `IAsyncRelayCommand<Folder> DeleteFolderCommand` (deletes folder
    **and** its sheets — the keep-sheets path is a separate command below,
    since the UI layer is the one that asks the user which they want and
    then calls the matching command)
  - `FolderViewModel(LibraryService libraryService, PdfImportService
    importService)`:
    `int FolderId` (settable, set before `LoadAsync`),
    `ObservableCollection<Sheet> Sheets`,
    `Task LoadAsync()`,
    `IAsyncRelayCommand<string> ImportPdfCommand`,
    `IAsyncRelayCommand<Sheet> DeleteSheetCommand`,
    `IAsyncRelayCommand<(Sheet Sheet, int? TargetFolderId)>
    MoveSheetCommand`

- [ ] **Step 1: Add CommunityToolkit.Mvvm**

```bash
dotnet add src/PiccoloReader.Core/PiccoloReader.Core.csproj package CommunityToolkit.Mvvm
```

- [ ] **Step 2: Write the failing tests for LibraryViewModel**

Create `tests/PiccoloReader.Core.Tests/ViewModels/LibraryViewModelTests.cs`:

```csharp
using PiccoloReader.Core.Data;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.ViewModels;

public class LibraryViewModelTests : IDisposable
{
    private readonly TestAppStorageProvider _storage = new();
    private readonly LibraryViewModel _sut;

    public LibraryViewModelTests()
    {
        Batteries_V2.Init();
        var database = new AppDatabase(_storage.DatabasePath);
        database.InitializeAsync().GetAwaiter().GetResult();
        var libraryService = new LibraryService(database, _storage);
        var importService = new PdfImportService(database, _storage);
        _sut = new LibraryViewModel(libraryService, importService);
    }

    public void Dispose() => _storage.Dispose();

    [Fact]
    public async Task CreateFolderCommand_AddsFolderToFoldersCollection()
    {
        _sut.NewFolderName = "Big Band";

        await _sut.CreateFolderCommand.ExecuteAsync(null);

        Assert.Single(_sut.Folders);
        Assert.Equal("Big Band", _sut.Folders[0].Name);
        Assert.Equal(string.Empty, _sut.NewFolderName);
    }

    [Fact]
    public async Task CreateFolderCommand_BlankName_DoesNotCreateFolder()
    {
        _sut.NewFolderName = "   ";

        await _sut.CreateFolderCommand.ExecuteAsync(null);

        Assert.Empty(_sut.Folders);
    }

    [Fact]
    public async Task ImportPdfCommand_AddsSheetToRootSheets()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(sourcePath, "fake-pdf");

        await _sut.ImportPdfCommand.ExecuteAsync(sourcePath);

        Assert.Single(_sut.RootSheets);
        File.Delete(sourcePath);
    }

    [Fact]
    public async Task DeleteFolderCommand_RemovesFolderFromCollection()
    {
        _sut.NewFolderName = "Tropical";
        await _sut.CreateFolderCommand.ExecuteAsync(null);
        var folder = _sut.Folders[0];

        await _sut.DeleteFolderCommand.ExecuteAsync(folder);

        Assert.Empty(_sut.Folders);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter LibraryViewModelTests`
Expected: FAIL (compile error — `LibraryViewModel` doesn't exist)

- [ ] **Step 4: Implement LibraryViewModel**

Create `src/PiccoloReader.Core/ViewModels/LibraryViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly LibraryService _libraryService;
    private readonly PdfImportService _importService;

    public LibraryViewModel(LibraryService libraryService, PdfImportService importService)
    {
        _libraryService = libraryService;
        _importService = importService;
    }

    public ObservableCollection<Folder> Folders { get; } = new();

    public ObservableCollection<Sheet> RootSheets { get; } = new();

    [ObservableProperty]
    private string _newFolderName = string.Empty;

    public async Task LoadAsync()
    {
        Folders.Clear();
        foreach (var folder in await _libraryService.GetFoldersAsync())
        {
            Folders.Add(folder);
        }

        RootSheets.Clear();
        foreach (var sheet in await _libraryService.GetSheetsAsync(null))
        {
            RootSheets.Add(sheet);
        }
    }

    [RelayCommand]
    private async Task CreateFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(NewFolderName))
        {
            return;
        }

        await _libraryService.CreateFolderAsync(NewFolderName);
        NewFolderName = string.Empty;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ImportPdfAsync(string sourceFilePath)
    {
        await _importService.ImportAsync(sourceFilePath, folderId: null);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteSheetAsync(Sheet sheet)
    {
        await _libraryService.DeleteSheetAsync(sheet);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteFolderAsync(Folder folder)
    {
        await _libraryService.DeleteFolderAsync(folder.Id, deleteSheets: true);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteFolderKeepSheetsAsync(Folder folder)
    {
        await _libraryService.DeleteFolderAsync(folder.Id, deleteSheets: false);
        await LoadAsync();
    }
}
```

- [ ] **Step 5: Run LibraryViewModel tests to verify they pass**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter LibraryViewModelTests`
Expected: PASS (4 tests)

- [ ] **Step 6: Write the failing tests for FolderViewModel**

Create `tests/PiccoloReader.Core.Tests/ViewModels/FolderViewModelTests.cs`:

```csharp
using PiccoloReader.Core.Data;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;
using SQLitePCL;

namespace PiccoloReader.Core.Tests.ViewModels;

public class FolderViewModelTests : IDisposable
{
    private readonly TestAppStorageProvider _storage = new();
    private readonly LibraryService _libraryService;
    private readonly FolderViewModel _sut;

    public FolderViewModelTests()
    {
        Batteries_V2.Init();
        var database = new AppDatabase(_storage.DatabasePath);
        database.InitializeAsync().GetAwaiter().GetResult();
        _libraryService = new LibraryService(database, _storage);
        var importService = new PdfImportService(database, _storage);
        _sut = new FolderViewModel(_libraryService, importService);
    }

    public void Dispose() => _storage.Dispose();

    [Fact]
    public async Task LoadAsync_PopulatesSheetsForFolderId()
    {
        var folder = await _libraryService.CreateFolderAsync("Orchestra");
        _sut.FolderId = folder.Id;
        var sourcePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(sourcePath, "fake-pdf");
        await _sut.ImportPdfCommand.ExecuteAsync(sourcePath);

        await _sut.LoadAsync();

        Assert.Single(_sut.Sheets);
        File.Delete(sourcePath);
    }

    [Fact]
    public async Task MoveSheetCommand_MovesSheetOutOfCurrentFolder()
    {
        var folder = await _libraryService.CreateFolderAsync("Orchestra");
        _sut.FolderId = folder.Id;
        var sourcePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(sourcePath, "fake-pdf");
        await _sut.ImportPdfCommand.ExecuteAsync(sourcePath);
        var sheet = _sut.Sheets[0];

        await _sut.MoveSheetCommand.ExecuteAsync((sheet, (int?)null));

        Assert.Empty(_sut.Sheets);
        File.Delete(sourcePath);
    }
}
```

- [ ] **Step 7: Run tests to verify they fail**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter FolderViewModelTests`
Expected: FAIL (compile error — `FolderViewModel` doesn't exist)

- [ ] **Step 8: Implement FolderViewModel**

Create `src/PiccoloReader.Core/ViewModels/FolderViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.ViewModels;

public partial class FolderViewModel : ObservableObject
{
    private readonly LibraryService _libraryService;
    private readonly PdfImportService _importService;

    public FolderViewModel(LibraryService libraryService, PdfImportService importService)
    {
        _libraryService = libraryService;
        _importService = importService;
    }

    [ObservableProperty]
    private int _folderId;

    public ObservableCollection<Sheet> Sheets { get; } = new();

    public async Task LoadAsync()
    {
        Sheets.Clear();
        foreach (var sheet in await _libraryService.GetSheetsAsync(FolderId))
        {
            Sheets.Add(sheet);
        }
    }

    [RelayCommand]
    private async Task ImportPdfAsync(string sourceFilePath)
    {
        await _importService.ImportAsync(sourceFilePath, FolderId);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteSheetAsync(Sheet sheet)
    {
        await _libraryService.DeleteSheetAsync(sheet);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task MoveSheetAsync((Sheet Sheet, int? TargetFolderId) args)
    {
        await _libraryService.MoveSheetAsync(args.Sheet, args.TargetFolderId);
        await LoadAsync();
    }
}
```

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter FolderViewModelTests`
Expected: PASS (2 tests)

- [ ] **Step 10: Run the full test suite**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
Expected: PASS (all tests, 16 total across the project so far)

- [ ] **Step 11: Commit**

```bash
git add src/PiccoloReader.Core tests/PiccoloReader.Core.Tests
git commit -m "feat: add LibraryViewModel and FolderViewModel"
git push
```

---

## Task 6: DI wiring, navigation skeleton, and Library page

**Files:**
- Create: `src/PiccoloReader/Services/MauiAppStorageProvider.cs`
- Create: `src/PiccoloReader/Views/LibraryPage.xaml` (+
  `LibraryPage.xaml.cs`)
- Create: `src/PiccoloReader/Views/FolderPage.xaml` (+
  `FolderPage.xaml.cs`)
- Modify: `src/PiccoloReader/MauiProgram.cs` (register DI services)
- Modify: `src/PiccoloReader/AppShell.xaml` (+ `.cs`) (route to
  `LibraryPage` instead of the template `MainPage`, register `FolderPage`
  route)
- Delete: `src/PiccoloReader/MainPage.xaml`,
  `src/PiccoloReader/MainPage.xaml.cs` (template placeholder, replaced by
  `LibraryPage`)

**Interfaces:**
- Consumes: `LibraryService`, `PdfImportService`, `LibraryViewModel`,
  `FolderViewModel` (Tasks 3–5), `IAppStorageProvider` (Task 2)
- Produces: a running app whose home screen is `LibraryPage`, and a
  registered `"folder"` Shell route to `FolderPage` that accepts a
  `FolderId` query parameter.

This task has no Core unit tests (it's MAUI UI wiring) — verified by
running the app, per the spec's testing section (no UI automation
framework in this design).

- [ ] **Step 1: Implement MauiAppStorageProvider**

Create `src/PiccoloReader/Services/MauiAppStorageProvider.cs`:

```csharp
using PiccoloReader.Core.Services;

namespace PiccoloReader.Services;

public class MauiAppStorageProvider : IAppStorageProvider
{
    public string DatabasePath => Path.Combine(FileSystem.AppDataDirectory, "piccoloreader.db3");

    public string SheetsDirectory => Path.Combine(FileSystem.AppDataDirectory, "Sheets");
}
```

- [ ] **Step 2: Register services and ViewModels in MauiProgram**

Modify `src/PiccoloReader/MauiProgram.cs` — add these `using`s at the top:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using PiccoloReader.Core.Data;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;
using PiccoloReader.Services;
using PiccoloReader.Views;
using SQLitePCL;
```

Insert this block into `CreateMauiApp()`, between `builder.Logging...`
and `return builder.Build();`:

```csharp
        Batteries_V2.Init();

        builder.Services.AddSingleton<IAppStorageProvider, MauiAppStorageProvider>();
        builder.Services.AddSingleton(sp =>
        {
            var storage = sp.GetRequiredService<IAppStorageProvider>();
            var database = new AppDatabase(storage.DatabasePath);
            database.InitializeAsync().GetAwaiter().GetResult();
            return database;
        });
        builder.Services.AddSingleton<LibraryService>();
        builder.Services.AddSingleton<PdfImportService>();

        builder.Services.AddTransient<LibraryViewModel>();
        builder.Services.AddTransient<FolderViewModel>();

        builder.Services.AddTransient<LibraryPage>();
        builder.Services.AddTransient<FolderPage>();
```

- [ ] **Step 3: Remove the template MainPage**

```bash
rm src/PiccoloReader/MainPage.xaml src/PiccoloReader/MainPage.xaml.cs
```

- [ ] **Step 4: Create LibraryPage.xaml**

Create `src/PiccoloReader/Views/LibraryPage.xaml`:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ContentPage
    x:Class="PiccoloReader.Views.LibraryPage"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    Title="Library">

    <Grid RowDefinitions="Auto,Auto,*" Padding="16" RowSpacing="12">

        <HorizontalStackLayout Grid.Row="0" Spacing="8">
            <Entry Placeholder="New folder name" Text="{Binding NewFolderName}" WidthRequest="220" />
            <Button Text="Create Folder" Command="{Binding CreateFolderCommand}" />
        </HorizontalStackLayout>

        <Button Grid.Row="1" Text="Import PDF" Clicked="OnImportPdfClicked" />

        <CollectionView Grid.Row="2" ItemsSource="{Binding Folders}">
            <CollectionView.Header>
                <Label Text="Folders" FontAttributes="Bold" Margin="0,0,0,4" />
            </CollectionView.Header>
            <CollectionView.ItemTemplate>
                <DataTemplate>
                    <Grid Padding="4" ColumnDefinitions="*,Auto">
                        <Label Text="{Binding Name}" VerticalOptions="Center">
                            <Label.GestureRecognizers>
                                <TapGestureRecognizer Tapped="OnFolderTapped" CommandParameter="{Binding}" />
                            </Label.GestureRecognizers>
                        </Label>
                        <Button Grid.Column="1" Text="Delete" Clicked="OnDeleteFolderClicked" CommandParameter="{Binding}" />
                    </Grid>
                </DataTemplate>
            </CollectionView.ItemTemplate>
        </CollectionView>
    </Grid>
</ContentPage>
```

- [ ] **Step 5: Create LibraryPage.xaml.cs**

Create `src/PiccoloReader/Views/LibraryPage.xaml.cs`:

```csharp
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Views;

public partial class LibraryPage : ContentPage
{
    private readonly LibraryViewModel _viewModel;

    public LibraryPage(LibraryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnImportPdfClicked(object? sender, EventArgs e)
    {
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Select a PDF",
            FileTypes = FilePickerFileType.Pdf
        });

        if (result is not null)
        {
            await _viewModel.ImportPdfCommand.ExecuteAsync(result.FullPath);
        }
    }

    private async void OnFolderTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is Folder folder)
        {
            await Shell.Current.GoToAsync($"folder?folderId={folder.Id}&folderName={folder.Name}");
        }
    }

    private async void OnDeleteFolderClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: Folder folder })
        {
            return;
        }

        var deleteSheets = await DisplayAlert(
            "Delete folder",
            $"Delete \"{folder.Name}\" and all sheets inside it? Choose \"Keep Sheets\" to move them to the root instead.",
            "Delete Sheets",
            "Keep Sheets");

        if (deleteSheets)
        {
            await _viewModel.DeleteFolderCommand.ExecuteAsync(folder);
        }
        else
        {
            await _viewModel.DeleteFolderKeepSheetsCommand.ExecuteAsync(folder);
        }
    }
}
```

- [ ] **Step 6: Create FolderPage.xaml**

Create `src/PiccoloReader/Views/FolderPage.xaml`:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ContentPage
    x:Class="PiccoloReader.Views.FolderPage"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    Title="Folder">

    <Grid RowDefinitions="Auto,*" Padding="16" RowSpacing="12">

        <Button Grid.Row="0" Text="Import PDF" Clicked="OnImportPdfClicked" />

        <CollectionView Grid.Row="1" ItemsSource="{Binding Sheets}">
            <CollectionView.ItemTemplate>
                <DataTemplate>
                    <Grid Padding="4" ColumnDefinitions="*,Auto,Auto">
                        <Label Text="{Binding Title}" VerticalOptions="Center" />
                        <Button Grid.Column="1" Text="Move to Root" Clicked="OnMoveToRootClicked" CommandParameter="{Binding}" />
                        <Button Grid.Column="2" Text="Delete" Clicked="OnDeleteSheetClicked" CommandParameter="{Binding}" />
                    </Grid>
                </DataTemplate>
            </CollectionView.ItemTemplate>
        </CollectionView>
    </Grid>
</ContentPage>
```

- [ ] **Step 7: Create FolderPage.xaml.cs**

Create `src/PiccoloReader/Views/FolderPage.xaml.cs`:

```csharp
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Views;

[QueryProperty(nameof(FolderId), "folderId")]
[QueryProperty(nameof(FolderName), "folderName")]
public partial class FolderPage : ContentPage
{
    private readonly FolderViewModel _viewModel;

    public FolderPage(FolderViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    public string FolderId
    {
        set => _viewModel.FolderId = int.Parse(value);
    }

    public string FolderName
    {
        set => Title = value;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnImportPdfClicked(object? sender, EventArgs e)
    {
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Select a PDF",
            FileTypes = FilePickerFileType.Pdf
        });

        if (result is not null)
        {
            await _viewModel.ImportPdfCommand.ExecuteAsync(result.FullPath);
        }
    }

    private async void OnMoveToRootClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: Sheet sheet })
        {
            await _viewModel.MoveSheetCommand.ExecuteAsync((sheet, (int?)null));
        }
    }

    private async void OnDeleteSheetClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: Sheet sheet })
        {
            await _viewModel.DeleteSheetCommand.ExecuteAsync(sheet);
        }
    }
}
```

- [ ] **Step 8: Wire AppShell**

Replace the contents of `src/PiccoloReader/AppShell.xaml`:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<Shell
    x:Class="PiccoloReader.AppShell"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:views="clr-namespace:PiccoloReader.Views"
    Title="PiccoloReader">

    <ShellContent
        Title="Library"
        ContentTemplate="{DataTemplate views:LibraryPage}"
        Route="library" />

</Shell>
```

Modify `src/PiccoloReader/AppShell.xaml.cs` to register the `FolderPage`
route in the constructor:

```csharp
using PiccoloReader.Views;

namespace PiccoloReader;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("folder", typeof(FolderPage));
    }
}
```

- [ ] **Step 9: Build the app**

Run: `dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android`
Expected: builds successfully

- [ ] **Step 10: Manual verification**

Run the app on an Android emulator (or connected device):

```bash
dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android -t:Run
```

Verify:
- App opens directly to the Library page (no template "Click me" screen).
- Typing a name and tapping "Create Folder" adds it to the folders list.
- Tapping a folder navigates to its (empty) Folder page.
- Tapping "Import PDF" opens the file picker and importing succeeds
  without error (the imported sheet isn't visible on the Library page
  yet — that's Task 7, which adds the root-sheets list this page is
  currently missing).

- [ ] **Step 11: Run the full Core test suite once more (regression check)**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
Expected: PASS (all tests)

- [ ] **Step 12: Commit**

```bash
git add src/PiccoloReader
git commit -m "feat: wire DI, navigation, and Library/Folder pages"
git push
```

---

## Task 7: Root sheets list, sheet move-to-folder, and folder delete prompt polish

**Files:**
- Modify: `src/PiccoloReader/Views/LibraryPage.xaml` (+ `.xaml.cs`) (add
  root-sheets section with delete action)
- Modify: `src/PiccoloReader/Views/FolderPage.xaml` (+ `.xaml.cs`) (move
  action becomes "Move to folder…" picking from all folders, not just
  root)

**Interfaces:**
- Consumes: `LibraryViewModel.RootSheets`, `LibraryViewModel
  .DeleteSheetCommand` (Task 5); `FolderViewModel.MoveSheetCommand`
  (Task 5); `LibraryService.GetFoldersAsync()` (Task 3, via
  `LibraryViewModel.Folders` already loaded)

This task closes the two gaps noted at the end of Task 6: the Library
page's root-sheets list, and letting a sheet move to a specific folder
(not only "move to root").

- [ ] **Step 1: Add the root-sheets section to LibraryPage.xaml**

In `src/PiccoloReader/Views/LibraryPage.xaml`, change the outer `Grid`'s
`RowDefinitions` from `"Auto,Auto,*"` to `"Auto,Auto,*,*"`, and add a
second `CollectionView` after the folders one (as the new `Grid.Row="3"`):

```xml
        <CollectionView Grid.Row="3" ItemsSource="{Binding RootSheets}">
            <CollectionView.Header>
                <Label Text="Sheets" FontAttributes="Bold" Margin="0,8,0,4" />
            </CollectionView.Header>
            <CollectionView.ItemTemplate>
                <DataTemplate>
                    <Grid Padding="4" ColumnDefinitions="*,Auto">
                        <Label Text="{Binding Title}" VerticalOptions="Center" />
                        <Button Grid.Column="1" Text="Delete" Clicked="OnDeleteRootSheetClicked" CommandParameter="{Binding}" />
                    </Grid>
                </DataTemplate>
            </CollectionView.ItemTemplate>
        </CollectionView>
```

The folders `CollectionView` keeps its existing `Grid.Row="2"` — only
the new sheets list is `Grid.Row="3"`.

- [ ] **Step 2: Handle the delete button in LibraryPage.xaml.cs**

Add this method to `src/PiccoloReader/Views/LibraryPage.xaml.cs`:

```csharp
    private async void OnDeleteRootSheetClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: Sheet sheet })
        {
            await _viewModel.DeleteSheetCommand.ExecuteAsync(sheet);
        }
    }
```

(Add `using PiccoloReader.Core.Data.Models;` if not already present —
it already is, from Task 6.)

- [ ] **Step 3: Replace "Move to Root" with "Move to folder…" in FolderPage**

In `src/PiccoloReader/Views/FolderPage.xaml`, change the button text
from `Move to Root` to `Move…`:

```xml
                        <Button Grid.Column="1" Text="Move…" Clicked="OnMoveSheetClicked" CommandParameter="{Binding}" />
```

- [ ] **Step 4: Implement the folder picker in FolderPage.xaml.cs**

In `src/PiccoloReader/Views/FolderPage.xaml.cs`, replace
`OnMoveToRootClicked` with:

```csharp
    private async void OnMoveSheetClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: Sheet sheet })
        {
            return;
        }

        var folders = await App.Current!.Handler!.MauiContext!.Services
            .GetRequiredService<PiccoloReader.Core.Services.LibraryService>()
            .GetFoldersAsync();

        var options = new List<string> { "Root" };
        options.AddRange(folders.Select(f => f.Name));

        var choice = await DisplayActionSheet("Move to…", "Cancel", null, options.ToArray());

        if (choice is null || choice == "Cancel")
        {
            return;
        }

        int? targetFolderId = choice == "Root"
            ? null
            : folders.First(f => f.Name == choice).Id;

        await _viewModel.MoveSheetCommand.ExecuteAsync((sheet, targetFolderId));
    }
```

Add `using Microsoft.Extensions.DependencyInjection;` to the top of the
file for `GetRequiredService`.

- [ ] **Step 5: Build the app**

Run: `dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android`
Expected: builds successfully

- [ ] **Step 6: Manual verification**

Run the app (`dotnet build src/PiccoloReader/PiccoloReader.csproj -f
net10.0-android -t:Run`) and verify:
- Importing a PDF at root shows it in the Library page's "Sheets" list.
- Deleting a root sheet removes it from the list.
- Inside a folder, "Move…" shows an action sheet listing "Root" plus all
  folders; picking one moves the sheet there (it disappears from the
  current folder's list).

- [ ] **Step 7: Run the full Core test suite (regression check)**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
Expected: PASS (all tests — this task touched no Core code, so this
confirms nothing broke)

- [ ] **Step 8: Commit**

```bash
git add src/PiccoloReader
git commit -m "feat: root sheets list and move-to-folder picker"
git push
```

- [ ] **Step 9: Mark the PR ready and merge**

```bash
gh pr checks --watch
gh pr merge --squash
```

Expected: CI passes, PR merges into `main`. This completes Plan 1 — the
app now has a fully working local library manager (create/delete
folders with the keep/delete-sheets prompt, import/move/delete sheets),
ready for Plan 2 (PDF viewer) to build on.
