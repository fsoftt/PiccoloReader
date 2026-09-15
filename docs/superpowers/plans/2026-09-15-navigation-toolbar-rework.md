# Navigation & Toolbar Rework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rework the already-merged Library/Folder pages to match the
redefined UX: a real Shell flyout (hamburger) menu, toolbar (appbar) icons
replacing the old inline buttons, long-press action menus replacing the old
per-row buttons, and client-side sort — plus finally show the root-level
sheets list on the Library page (deferred from the original plan).

**Architecture:** No changes to the Core service layer
(`LibraryService`, `PdfImportService`) or the data model — this plan is
almost entirely Presentation-layer rework, plus two small ViewModel
additions (sort, and a `MoveSheetCommand` on `LibraryViewModel` to support
moving a root sheet into a folder, which didn't exist before since only
folder-to-root movement existed previously).

**Tech Stack:** .NET 10 (SDK 10.0.401), .NET MAUI, `CommunityToolkit.Mvvm`
(already in use), `CommunityToolkit.Maui` (new — provides the long-press
gesture MAUI itself doesn't have).

**Spec:** [`docs/superpowers/specs/2026-09-14-architecture-design.md`](../specs/2026-09-14-architecture-design.md)
(also see [`docs/requirements.md`](../../requirements.md), both updated for
this redefinition)

## Global Constraints

- Folders have no date field and won't get one for this plan — sort-by-date
  applies only to sheet lists (`RootSheets` on the Library page, `Sheets`
  on the Folder page); the folders list stays alphabetical always (per
  spec).
- Toolbar items use **text labels** (`ToolbarItem.Text`), not icon images —
  no icon asset pipeline exists in this repo yet, and the music icon set is
  already a tracked open item. Real icon glyphs are a future visual-polish
  item, out of scope here.
- Tapping a **sheet** (Library or Folder page) does nothing yet — the Sheet
  Viewer it would open doesn't exist until a later plan. Only long-press
  (Move/Delete) is wired for sheets in this plan. Tapping a **folder**
  still navigates to the Folder page (unchanged, already works).
- `CommunityToolkit.Maui`'s `TouchBehavior.LongPressCommand` is an
  `ICommand` (not an event) — bind it via `x:Reference` to a `Command`
  exposed on the page's code-behind, since the item's own `BindingContext`
  in a `DataTemplate` is the list item (`Folder`/`Sheet`), not the page.
- Every long-press action goes through `DisplayActionSheet`, including the
  folder delete choice (**Delete Sheets** / **Keep Sheets** / **Cancel**) —
  this replaces the old two-button `DisplayAlertAsync` version of that
  prompt, which could only offer two choices, not a true three-way one.

---

## Task 1: Add CommunityToolkit.Maui and a real Shell flyout

**Files:**
- Modify: `src/PiccoloReader/PiccoloReader.csproj` (add package)
- Modify: `src/PiccoloReader/MauiProgram.cs` (call `UseMauiCommunityToolkit()`)
- Modify: `src/PiccoloReader/AppShell.xaml` (wrap in a `FlyoutItem`, force the
  flyout on)

**Interfaces:**
- Produces: `builder.UseMauiCommunityToolkit()` called in
  `MauiProgram.CreateMauiApp()`, making `CommunityToolkit.Maui.Behaviors.*`
  usable from XAML app-wide (needed by Tasks 3–4).

No Core unit tests — this is app-head wiring, verified by running the app.

- [ ] **Step 1: Add the CommunityToolkit.Maui package**

Pinned to 13.0.0, not latest: the latest release (15.0.1 at time of
writing) requires `Microsoft.Maui.Controls >= 10.0.90`, which downgrades
against the `Microsoft.Maui.Controls 10.0.20` this project's installed
MAUI workload provides (`NU1605`, a hard restore error, not just a
warning). 13.0.0 is the newest release whose floor (`>= 10.0.10`) the
current workload satisfies — confirmed by checking each version's nuspec
on nuget.org, not by trial and error.

```bash
dotnet add src/PiccoloReader/PiccoloReader.csproj package CommunityToolkit.Maui --version 13.0.0
```

- [ ] **Step 2: Call UseMauiCommunityToolkit() in MauiProgram**

In `src/PiccoloReader/MauiProgram.cs`, add `using CommunityToolkit.Maui;` to
the `using`s, and insert `.UseMauiCommunityToolkit()` into the builder
chain:

```csharp
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});
```

- [ ] **Step 3: Rework AppShell.xaml into a real flyout**

Replace the contents of `src/PiccoloReader/AppShell.xaml`:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<Shell
    x:Class="PiccoloReader.AppShell"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:views="clr-namespace:PiccoloReader.Views"
    FlyoutBehavior="Flyout"
    Title="PiccoloReader">

    <FlyoutItem Title="Library" Route="library">
        <ShellContent ContentTemplate="{DataTemplate views:LibraryPage}" />
    </FlyoutItem>

</Shell>
```

`FlyoutBehavior="Flyout"` forces the hamburger icon to always show, even
with a single item — the menu is meant to grow (per the spec), so this
isn't a one-item special case to optimize away.

`AppShell.xaml.cs` needs no change — it still registers the `"folder"`
route in its constructor.

- [ ] **Step 4: Build the app**

```bash
dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android
```

Expected: builds successfully.

- [ ] **Step 5: Manual verification**

Run the app on an Android emulator. Verify:
- A hamburger icon appears in the top-left of the toolbar.
- Tapping it opens a flyout drawer showing "Library".
- Tapping "Library" (or it already being the active page) shows the
  Library page underneath.

- [ ] **Step 6: Run the full Core test suite (regression check)**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj
```

Expected: PASS (16 tests — this task touched no Core code)

- [ ] **Step 7: Commit**

```bash
git add src/PiccoloReader/PiccoloReader.csproj src/PiccoloReader/MauiProgram.cs src/PiccoloReader/AppShell.xaml
git commit -m "feat: add CommunityToolkit.Maui and a real Shell flyout"
git push
```

---

## Task 2: Sort support and LibraryViewModel.MoveSheetCommand

**Files:**
- Create: `src/PiccoloReader.Core/ViewModels/SortOptions.cs`
- Modify: `src/PiccoloReader.Core/ViewModels/LibraryViewModel.cs`
- Modify: `src/PiccoloReader.Core/ViewModels/FolderViewModel.cs`
- Modify: `tests/PiccoloReader.Core.Tests/ViewModels/LibraryViewModelTests.cs`
- Modify: `tests/PiccoloReader.Core.Tests/ViewModels/FolderViewModelTests.cs`

**Interfaces:**
- Consumes: `LibraryViewModel`, `FolderViewModel` (existing, from Plan 1)
- Produces:
  - `SortField { Name, DateAdded }`, `SortDirection { Ascending,
    Descending }` (both in `PiccoloReader.Core.ViewModels`)
  - `LibraryViewModel`: `SortField SortField` (bindable, default `Name`),
    `SortDirection SortDirection` (bindable, default `Ascending`),
    `void ApplySort(SortField field, SortDirection direction)` (re-sorts
    `RootSheets` in place; `Folders` is untouched), and a new
    `IAsyncRelayCommand<(Sheet Sheet, int? TargetFolderId)> MoveSheetCommand`
  - `FolderViewModel`: same `SortField`/`SortDirection`/`ApplySort`
    members, operating on `Sheets` instead of `RootSheets`

- [ ] **Step 1: Create the sort enums**

Create `src/PiccoloReader.Core/ViewModels/SortOptions.cs`:

```csharp
namespace PiccoloReader.Core.ViewModels;

public enum SortField
{
    Name,
    DateAdded
}

public enum SortDirection
{
    Ascending,
    Descending
}
```

- [ ] **Step 2: Write the failing tests for LibraryViewModel's sort and move**

Add these test methods to
`tests/PiccoloReader.Core.Tests/ViewModels/LibraryViewModelTests.cs`
(inside the existing `LibraryViewModelTests` class, alongside the current
tests — add `using PiccoloReader.Core.Data.Models;` to the top if not
already present):

```csharp
    [Fact]
    public void ApplySort_NameDescending_OrdersRootSheetsReverseAlphabetically()
    {
        _sut.RootSheets.Add(new Sheet { Title = "A-Piece", DateAdded = DateTime.UtcNow });
        _sut.RootSheets.Add(new Sheet { Title = "Z-Piece", DateAdded = DateTime.UtcNow });

        _sut.ApplySort(SortField.Name, SortDirection.Descending);

        Assert.Equal("Z-Piece", _sut.RootSheets[0].Title);
        Assert.Equal("A-Piece", _sut.RootSheets[1].Title);
    }

    [Fact]
    public void ApplySort_DateAddedDescending_OrdersRootSheetsNewestFirst()
    {
        _sut.RootSheets.Add(new Sheet { Title = "Older", DateAdded = DateTime.UtcNow.AddDays(-1) });
        _sut.RootSheets.Add(new Sheet { Title = "Newer", DateAdded = DateTime.UtcNow });

        _sut.ApplySort(SortField.DateAdded, SortDirection.Descending);

        Assert.Equal("Newer", _sut.RootSheets[0].Title);
        Assert.Equal("Older", _sut.RootSheets[1].Title);
    }

    [Fact]
    public async Task MoveSheetCommand_MovesRootSheetIntoFolder()
    {
        _sut.NewFolderName = "Orchestra";
        await _sut.CreateFolderCommand.ExecuteAsync(null);
        var folder = _sut.Folders[0];

        var sourcePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(sourcePath, "fake-pdf");
        await _sut.ImportPdfCommand.ExecuteAsync(sourcePath);
        var sheet = _sut.RootSheets[0];

        await _sut.MoveSheetCommand.ExecuteAsync((sheet, (int?)folder.Id));

        Assert.Empty(_sut.RootSheets);
        File.Delete(sourcePath);
    }
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter LibraryViewModelTests
```

Expected: FAIL (compile error — `ApplySort`, `SortField`, `SortDirection`,
`MoveSheetCommand` don't exist yet on `LibraryViewModel`)

- [ ] **Step 4: Implement sort and move on LibraryViewModel**

Modify `src/PiccoloReader.Core/ViewModels/LibraryViewModel.cs`. Add
`[ObservableProperty] private SortField _sortField = SortField.Name;` and
`[ObservableProperty] private SortDirection _sortDirection =
SortDirection.Ascending;` alongside the existing `NewFolderName` property,
and add these two members after `DeleteFolderKeepSheetsAsync`:

```csharp
    public void ApplySort(SortField field, SortDirection direction)
    {
        SortField = field;
        SortDirection = direction;

        var sorted = field switch
        {
            SortField.DateAdded => direction == SortDirection.Ascending
                ? RootSheets.OrderBy(s => s.DateAdded).ToList()
                : RootSheets.OrderByDescending(s => s.DateAdded).ToList(),
            _ => direction == SortDirection.Ascending
                ? RootSheets.OrderBy(s => s.Title).ToList()
                : RootSheets.OrderByDescending(s => s.Title).ToList()
        };

        RootSheets.Clear();
        foreach (var sheet in sorted)
        {
            RootSheets.Add(sheet);
        }
    }

    [RelayCommand]
    private async Task MoveSheetAsync((Sheet Sheet, int? TargetFolderId) args)
    {
        await _libraryService.MoveSheetAsync(args.Sheet, args.TargetFolderId);
        await LoadAsync();
    }
```

- [ ] **Step 5: Run LibraryViewModel tests to verify they pass**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter LibraryViewModelTests
```

Expected: PASS (7 tests — 4 existing + 3 new)

- [ ] **Step 6: Write the failing test for FolderViewModel's sort**

Add this test method to
`tests/PiccoloReader.Core.Tests/ViewModels/FolderViewModelTests.cs` (inside
the existing `FolderViewModelTests` class):

```csharp
    [Fact]
    public void ApplySort_NameDescending_OrdersSheetsReverseAlphabetically()
    {
        _sut.Sheets.Add(new Sheet { Title = "A-Piece", DateAdded = DateTime.UtcNow });
        _sut.Sheets.Add(new Sheet { Title = "Z-Piece", DateAdded = DateTime.UtcNow });

        _sut.ApplySort(SortField.Name, SortDirection.Descending);

        Assert.Equal("Z-Piece", _sut.Sheets[0].Title);
        Assert.Equal("A-Piece", _sut.Sheets[1].Title);
    }
```

- [ ] **Step 7: Run test to verify it fails**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter FolderViewModelTests
```

Expected: FAIL (compile error — `ApplySort` doesn't exist on
`FolderViewModel`)

- [ ] **Step 8: Implement sort on FolderViewModel**

Modify `src/PiccoloReader.Core/ViewModels/FolderViewModel.cs`. Add
`[ObservableProperty] private SortField _sortField = SortField.Name;` and
`[ObservableProperty] private SortDirection _sortDirection =
SortDirection.Ascending;` alongside the existing `FolderId` property, and
add this method after `MoveSheetAsync`:

```csharp
    public void ApplySort(SortField field, SortDirection direction)
    {
        SortField = field;
        SortDirection = direction;

        var sorted = field switch
        {
            SortField.DateAdded => direction == SortDirection.Ascending
                ? Sheets.OrderBy(s => s.DateAdded).ToList()
                : Sheets.OrderByDescending(s => s.DateAdded).ToList(),
            _ => direction == SortDirection.Ascending
                ? Sheets.OrderBy(s => s.Title).ToList()
                : Sheets.OrderByDescending(s => s.Title).ToList()
        };

        Sheets.Clear();
        foreach (var sheet in sorted)
        {
            Sheets.Add(sheet);
        }
    }
```

- [ ] **Step 9: Run tests to verify they pass**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter FolderViewModelTests
```

Expected: PASS (3 tests — 2 existing + 1 new)

- [ ] **Step 10: Run the full test suite**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj
```

Expected: PASS (20 tests total)

- [ ] **Step 11: Commit**

```bash
git add src/PiccoloReader.Core/ViewModels tests/PiccoloReader.Core.Tests/ViewModels
git commit -m "feat: add sort support and LibraryViewModel.MoveSheetCommand"
git push
```

---

## Task 3: Rework the Library page

**Files:**
- Modify: `src/PiccoloReader/Views/LibraryPage.xaml` (full rewrite)
- Modify: `src/PiccoloReader/Views/LibraryPage.xaml.cs` (full rewrite)

**Interfaces:**
- Consumes: `LibraryViewModel` (Task 2's `ApplySort`, `MoveSheetCommand`,
  `SortField`/`SortDirection`; existing `Folders`, `RootSheets`,
  `NewFolderName`, `CreateFolderCommand`, `ImportPdfCommand`,
  `DeleteFolderCommand`, `DeleteFolderKeepSheetsCommand`,
  `DeleteSheetCommand`), `LibraryService.GetFoldersAsync()` (for the move
  picker's folder list)

No Core unit tests — this is UI, verified by running the app.

- [ ] **Step 1: Replace LibraryPage.xaml**

Replace the entire contents of `src/PiccoloReader/Views/LibraryPage.xaml`:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ContentPage
    x:Class="PiccoloReader.Views.LibraryPage"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:toolkit="http://schemas.microsoft.com/dotnet/2022/maui/toolkit"
    x:Name="ThisPage"
    Title="Library">

    <ContentPage.ToolbarItems>
        <ToolbarItem Text="Import" Clicked="OnImportPdfClicked" />
        <ToolbarItem Text="Create Folder" Clicked="OnCreateFolderClicked" />
        <ToolbarItem Text="Sort" Clicked="OnSortClicked" />
    </ContentPage.ToolbarItems>

    <Grid RowDefinitions="Auto,Auto" Padding="16" RowSpacing="12">

        <CollectionView Grid.Row="0" ItemsSource="{Binding Folders}">
            <CollectionView.Header>
                <Label Text="Folders" FontAttributes="Bold" Margin="0,0,0,4" />
            </CollectionView.Header>
            <CollectionView.ItemTemplate>
                <DataTemplate>
                    <Grid Padding="4">
                        <Grid.GestureRecognizers>
                            <TapGestureRecognizer Tapped="OnFolderTapped" CommandParameter="{Binding}" />
                        </Grid.GestureRecognizers>
                        <Grid.Behaviors>
                            <toolkit:TouchBehavior
                                LongPressCommand="{Binding Source={x:Reference ThisPage}, Path=FolderLongPressCommand}"
                                LongPressCommandParameter="{Binding}" />
                        </Grid.Behaviors>
                        <Label Text="{Binding Name}" VerticalOptions="Center" />
                    </Grid>
                </DataTemplate>
            </CollectionView.ItemTemplate>
        </CollectionView>

        <CollectionView Grid.Row="1" ItemsSource="{Binding RootSheets}">
            <CollectionView.Header>
                <Label Text="Sheets" FontAttributes="Bold" Margin="0,8,0,4" />
            </CollectionView.Header>
            <CollectionView.ItemTemplate>
                <DataTemplate>
                    <Grid Padding="4">
                        <Grid.Behaviors>
                            <toolkit:TouchBehavior
                                LongPressCommand="{Binding Source={x:Reference ThisPage}, Path=SheetLongPressCommand}"
                                LongPressCommandParameter="{Binding}" />
                        </Grid.Behaviors>
                        <Label Text="{Binding Title}" VerticalOptions="Center" />
                    </Grid>
                </DataTemplate>
            </CollectionView.ItemTemplate>
        </CollectionView>
    </Grid>
</ContentPage>
```

- [ ] **Step 2: Replace LibraryPage.xaml.cs**

Replace the entire contents of
`src/PiccoloReader/Views/LibraryPage.xaml.cs`:

```csharp
using System.Windows.Input;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Views;

public partial class LibraryPage : ContentPage
{
    private readonly LibraryViewModel _viewModel;
    private readonly LibraryService _libraryService;

    public LibraryPage(LibraryViewModel viewModel, LibraryService libraryService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _libraryService = libraryService;
        BindingContext = _viewModel;

        FolderLongPressCommand = new Command<Folder>(async folder => await OnFolderLongPressedAsync(folder));
        SheetLongPressCommand = new Command<Sheet>(async sheet => await OnSheetLongPressedAsync(sheet));
    }

    public ICommand FolderLongPressCommand { get; }

    public ICommand SheetLongPressCommand { get; }

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

    private async void OnCreateFolderClicked(object? sender, EventArgs e)
    {
        var name = await DisplayPromptAsync("Create Folder", "Folder name:");

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _viewModel.NewFolderName = name;
        await _viewModel.CreateFolderCommand.ExecuteAsync(null);
    }

    private async void OnSortClicked(object? sender, EventArgs e)
    {
        var choice = await DisplayActionSheet(
            "Sort by",
            "Cancel",
            null,
            "Name (A-Z)",
            "Name (Z-A)",
            "Date Added (Oldest First)",
            "Date Added (Newest First)");

        (SortField Field, SortDirection Direction)? sort = choice switch
        {
            "Name (A-Z)" => (SortField.Name, SortDirection.Ascending),
            "Name (Z-A)" => (SortField.Name, SortDirection.Descending),
            "Date Added (Oldest First)" => (SortField.DateAdded, SortDirection.Ascending),
            "Date Added (Newest First)" => (SortField.DateAdded, SortDirection.Descending),
            _ => null
        };

        if (sort is { } selected)
        {
            _viewModel.ApplySort(selected.Field, selected.Direction);
        }
    }

    private async void OnFolderTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is Folder folder)
        {
            await Shell.Current.GoToAsync($"folder?folderId={folder.Id}&folderName={folder.Name}");
        }
    }

    private async Task OnFolderLongPressedAsync(Folder folder)
    {
        var choice = await DisplayActionSheet($"\"{folder.Name}\"", "Cancel", null, "Delete Sheets", "Keep Sheets");

        switch (choice)
        {
            case "Delete Sheets":
                await _viewModel.DeleteFolderCommand.ExecuteAsync(folder);
                break;
            case "Keep Sheets":
                await _viewModel.DeleteFolderKeepSheetsCommand.ExecuteAsync(folder);
                break;
        }
    }

    private async Task OnSheetLongPressedAsync(Sheet sheet)
    {
        var choice = await DisplayActionSheet($"\"{sheet.Title}\"", "Cancel", null, "Move", "Delete");

        if (choice == "Delete")
        {
            var confirmed = await DisplayAlertAsync(
                "Delete sheet",
                $"Delete \"{sheet.Title}\"? This cannot be undone.",
                "Delete",
                "Cancel");

            if (confirmed)
            {
                await _viewModel.DeleteSheetCommand.ExecuteAsync(sheet);
            }
        }
        else if (choice == "Move")
        {
            var folders = await _libraryService.GetFoldersAsync();

            if (folders.Count == 0)
            {
                await DisplayAlertAsync("Move", "Create a folder first to move sheets into.", "OK");
                return;
            }

            var target = await DisplayActionSheet("Move to…", "Cancel", null, folders.Select(f => f.Name).ToArray());

            if (target is null || target == "Cancel")
            {
                return;
            }

            var targetFolder = folders.First(f => f.Name == target);
            await _viewModel.MoveSheetCommand.ExecuteAsync((sheet, (int?)targetFolder.Id));
        }
    }
}
```

- [ ] **Step 3: Build the app**

```bash
dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android
```

Expected: builds successfully.

- [ ] **Step 4: Manual verification**

Run the app on an Android emulator. Verify:
- Toolbar shows Import / Create Folder / Sort.
- "Create Folder" prompts for a name and adds it to the Folders list.
- "Import" opens the file picker and a successful import shows the sheet
  under "Sheets".
- Long-pressing a folder shows Delete Sheets / Keep Sheets / Cancel; each
  choice behaves as expected.
- Long-pressing a sheet shows Move / Delete / Cancel; Delete asks to
  confirm; Move (with at least one folder created) shows a folder picker
  and moves the sheet, removing it from the root list.
- Sort changes the order of the Sheets list; Folders stays alphabetical.

- [ ] **Step 5: Run the full Core test suite (regression check)**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj
```

Expected: PASS (20 tests — this task touched no Core code)

- [ ] **Step 6: Commit**

```bash
git add src/PiccoloReader/Views/LibraryPage.xaml src/PiccoloReader/Views/LibraryPage.xaml.cs
git commit -m "feat: rework Library page (toolbar, root sheets, long-press actions)"
git push
```

---

## Task 4: Rework the Folder page

**Files:**
- Modify: `src/PiccoloReader/Views/FolderPage.xaml` (full rewrite)
- Modify: `src/PiccoloReader/Views/FolderPage.xaml.cs` (full rewrite)

**Interfaces:**
- Consumes: `FolderViewModel` (Task 2's `ApplySort`, `MoveSheetCommand`,
  `SortField`/`SortDirection`; existing `Sheets`, `FolderId`,
  `ImportPdfCommand`, `DeleteSheetCommand`), `LibraryService.GetFoldersAsync()`

No Core unit tests — this is UI, verified by running the app.

- [ ] **Step 1: Replace FolderPage.xaml**

Replace the entire contents of `src/PiccoloReader/Views/FolderPage.xaml`:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ContentPage
    x:Class="PiccoloReader.Views.FolderPage"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:toolkit="http://schemas.microsoft.com/dotnet/2022/maui/toolkit"
    x:Name="ThisPage"
    Title="Folder">

    <ContentPage.ToolbarItems>
        <ToolbarItem Text="Import" Clicked="OnImportPdfClicked" />
        <ToolbarItem Text="Sort" Clicked="OnSortClicked" />
    </ContentPage.ToolbarItems>

    <Grid Padding="16">
        <CollectionView ItemsSource="{Binding Sheets}">
            <CollectionView.ItemTemplate>
                <DataTemplate>
                    <Grid Padding="4">
                        <Grid.Behaviors>
                            <toolkit:TouchBehavior
                                LongPressCommand="{Binding Source={x:Reference ThisPage}, Path=SheetLongPressCommand}"
                                LongPressCommandParameter="{Binding}" />
                        </Grid.Behaviors>
                        <Label Text="{Binding Title}" VerticalOptions="Center" />
                    </Grid>
                </DataTemplate>
            </CollectionView.ItemTemplate>
        </CollectionView>
    </Grid>
</ContentPage>
```

- [ ] **Step 2: Replace FolderPage.xaml.cs**

Replace the entire contents of
`src/PiccoloReader/Views/FolderPage.xaml.cs`:

```csharp
using System.Windows.Input;
using PiccoloReader.Core.Data.Models;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Views;

[QueryProperty(nameof(FolderId), "folderId")]
[QueryProperty(nameof(FolderName), "folderName")]
public partial class FolderPage : ContentPage
{
    private readonly FolderViewModel _viewModel;
    private readonly LibraryService _libraryService;

    public FolderPage(FolderViewModel viewModel, LibraryService libraryService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _libraryService = libraryService;
        BindingContext = _viewModel;

        SheetLongPressCommand = new Command<Sheet>(async sheet => await OnSheetLongPressedAsync(sheet));
    }

    public ICommand SheetLongPressCommand { get; }

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

    private async void OnSortClicked(object? sender, EventArgs e)
    {
        var choice = await DisplayActionSheet(
            "Sort by",
            "Cancel",
            null,
            "Name (A-Z)",
            "Name (Z-A)",
            "Date Added (Oldest First)",
            "Date Added (Newest First)");

        (SortField Field, SortDirection Direction)? sort = choice switch
        {
            "Name (A-Z)" => (SortField.Name, SortDirection.Ascending),
            "Name (Z-A)" => (SortField.Name, SortDirection.Descending),
            "Date Added (Oldest First)" => (SortField.DateAdded, SortDirection.Ascending),
            "Date Added (Newest First)" => (SortField.DateAdded, SortDirection.Descending),
            _ => null
        };

        if (sort is { } selected)
        {
            _viewModel.ApplySort(selected.Field, selected.Direction);
        }
    }

    private async Task OnSheetLongPressedAsync(Sheet sheet)
    {
        var choice = await DisplayActionSheet($"\"{sheet.Title}\"", "Cancel", null, "Move", "Delete");

        if (choice == "Delete")
        {
            var confirmed = await DisplayAlertAsync(
                "Delete sheet",
                $"Delete \"{sheet.Title}\"? This cannot be undone.",
                "Delete",
                "Cancel");

            if (confirmed)
            {
                await _viewModel.DeleteSheetCommand.ExecuteAsync(sheet);
            }
        }
        else if (choice == "Move")
        {
            var folders = await _libraryService.GetFoldersAsync();

            var options = new List<string> { "Root" };
            options.AddRange(folders.Where(f => f.Id != _viewModel.FolderId).Select(f => f.Name));

            var target = await DisplayActionSheet("Move to…", "Cancel", null, options.ToArray());

            if (target is null || target == "Cancel")
            {
                return;
            }

            int? targetFolderId = target == "Root"
                ? null
                : folders.First(f => f.Name == target).Id;

            await _viewModel.MoveSheetCommand.ExecuteAsync((sheet, targetFolderId));
        }
    }
}
```

- [ ] **Step 3: Build the app**

```bash
dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android
```

Expected: builds successfully.

- [ ] **Step 4: Manual verification**

Run the app on an Android emulator. Verify:
- Inside a folder, toolbar shows Import / Sort (no Create Folder).
- Import adds a sheet to the list.
- Long-pressing a sheet shows Move / Delete / Cancel.
- Move shows "Root" plus every *other* folder (not the current one),
  and moving works (sheet disappears from the current folder's list).
- Delete asks to confirm before removing the sheet.
- Sort changes the order of the list.

- [ ] **Step 5: Run the full Core test suite (regression check)**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj
```

Expected: PASS (20 tests — this task touched no Core code)

- [ ] **Step 6: Commit**

```bash
git add src/PiccoloReader/Views/FolderPage.xaml src/PiccoloReader/Views/FolderPage.xaml.cs
git commit -m "feat: rework Folder page (toolbar, long-press actions, full move picker)"
git push
```

- [ ] **Step 7: Open a PR and verify CI passes**

```bash
gh pr checks --watch
```

Expected: CI passes. This is the last task of this plan — once merged, the
Library/Folder pages match the redefined UX end to end.
