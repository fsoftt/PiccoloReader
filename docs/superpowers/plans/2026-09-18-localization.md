# Multi-Language Support (English + Spanish) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add English + Spanish localization to every user-facing string in the app, plus a "Settings" flyout item and a Settings page where the language can be changed (restart required to take effect).

**Architecture:** A hand-written `AppStrings` static class in `PiccoloReader.Core` wraps a `ResourceManager` over two `.resx` files (`AppStrings.resx` English, `AppStrings.es.resx` Spanish) — no IDE-generated Designer.cs, since this project builds entirely via CLI `dotnet build`. Language preference persists via MAUI `Preferences`, reached from Core through a new `ILanguagePreferenceService` interface (mirroring the existing `IAppStorageProvider` split). The active culture is resolved once at startup in `MauiProgram.cs` and never changes mid-session.

**Tech Stack:** .NET 10, .NET MAUI, `CommunityToolkit.Mvvm` (existing), plain `System.Resources.ResourceManager` (BCL, no new package), `Microsoft.Maui.Storage.Preferences` (already available via the existing `Microsoft.Maui.Controls` package reference).

**Spec:** [`docs/superpowers/specs/2026-09-18-localization-design.md`](../specs/2026-09-18-localization-design.md)

## Global Constraints

- Language switch is **restart-required**, not live — confirmed in the spec. No reactive/live-rebinding markup extension.
- Resource files live in **`PiccoloReader.Core`** (not the App project), because `MusicIconCatalog`'s strings are Core-only and Core cannot depend on the App project.
- No IDE-generated resx Designer.cs — a hand-written `AppStrings` static class instead (CLI-build-safe).
- Missing-key fallback: `AppStrings.Get` falls back to the key name itself, never throws, never returns empty.
- Language names in the Settings page ("English" / "Español") are **not** run through `AppStrings` — every app shows a language's own native name regardless of current UI language; this is deliberate, not an oversight.
- The 5 dynamics abbreviations in `MusicIconCatalog` (`pp`, `p`, `f`, `ff`, `fp`) stay as plain hardcoded literals — universal musical notation, not language-specific words, so nothing to translate.
- "Piccolo" wordmark, "PiccoloReader" app name, and the `@fsoftt` credit handle/URL are brand identity, never localized.
- No programmatic app restart — the Settings page shows a dialog asking the user to restart manually.
- Follow the existing `IAppStorageProvider`/`MauiAppStorageProvider` split exactly for the new `ILanguagePreferenceService`/`MauiLanguagePreferenceService`.

---

## Task 1: `AppStrings` — resource files and wrapper class

**Files:**
- Create: `src/PiccoloReader.Core/Resources/Strings/AppStrings.resx`
- Create: `src/PiccoloReader.Core/Resources/Strings/AppStrings.es.resx`
- Create: `src/PiccoloReader.Core/Resources/Strings/AppStrings.cs`
- Test: `tests/PiccoloReader.Core.Tests/Resources/AppStringsTests.cs`

**Interfaces:**
- Produces: `PiccoloReader.Core.Resources.Strings.AppStrings` — one static string-returning property per key listed below. Every later task consumes properties from this class by name.

No `.resx` culture-suffix wiring is needed in the `.csproj` — SDK-style projects embed any `.resx` file automatically, and a `Name.culture.resx` filename (e.g. `AppStrings.es.resx`) is recognized as a satellite resource for that culture by convention.

- [ ] **Step 1: Create the English resx file**

Create `src/PiccoloReader.Core/Resources/Strings/AppStrings.resx` with this exact content (standard empty resx schema header, then one `<data>` element per key):

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
    <xsd:element name="root" msdata:IsDataSet="true">
      <xsd:complexType>
        <xsd:choice maxOccurs="unbounded">
          <xsd:element name="data">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" msdata:Ordinal="1" />
              <xsd:attribute name="type" type="xsd:string" msdata:Ordinal="3" />
              <xsd:attribute name="mimetype" type="xsd:string" msdata:Ordinal="4" />
            </xsd:complexType>
          </xsd:element>
        </xsd:choice>
      </xsd:complexType>
    </xsd:element>
  </xsd:schema>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <data name="Library" xml:space="preserve"><value>Library</value></data>
  <data name="Settings" xml:space="preserve"><value>Settings</value></data>
  <data name="MadeWithLoveBy" xml:space="preserve"><value>Made with love by</value></data>
  <data name="Folders" xml:space="preserve"><value>Folders</value></data>
  <data name="Sheets" xml:space="preserve"><value>Sheets</value></data>
  <data name="SearchFoldersPlaceholder" xml:space="preserve"><value>Search folders</value></data>
  <data name="SearchSheetsPlaceholder" xml:space="preserve"><value>Search sheets</value></data>
  <data name="SelectPdfsPickerTitle" xml:space="preserve"><value>Select PDFs</value></data>
  <data name="CreateFolderTitle" xml:space="preserve"><value>Create Folder</value></data>
  <data name="FolderNamePrompt" xml:space="preserve"><value>Folder name:</value></data>
  <data name="SortFoldersByTitle" xml:space="preserve"><value>Sort folders by</value></data>
  <data name="SortSheetsByTitle" xml:space="preserve"><value>Sort sheets by</value></data>
  <data name="SortByTitle" xml:space="preserve"><value>Sort by</value></data>
  <data name="Cancel" xml:space="preserve"><value>Cancel</value></data>
  <data name="SortNameAscending" xml:space="preserve"><value>Name (A-Z)</value></data>
  <data name="SortNameDescending" xml:space="preserve"><value>Name (Z-A)</value></data>
  <data name="SortDateAddedAscending" xml:space="preserve"><value>Date Added (Oldest First)</value></data>
  <data name="SortDateAddedDescending" xml:space="preserve"><value>Date Added (Newest First)</value></data>
  <data name="DeleteSheetsOption" xml:space="preserve"><value>Delete Sheets</value></data>
  <data name="KeepSheetsOption" xml:space="preserve"><value>Keep Sheets</value></data>
  <data name="Move" xml:space="preserve"><value>Move</value></data>
  <data name="Delete" xml:space="preserve"><value>Delete</value></data>
  <data name="DeleteSheetTitle" xml:space="preserve"><value>Delete sheet</value></data>
  <data name="DeleteSheetMessageFormat" xml:space="preserve"><value>Delete "{0}"? This cannot be undone.</value></data>
  <data name="RootOption" xml:space="preserve"><value>Root</value></data>
  <data name="MoveToTitle" xml:space="preserve"><value>Move to…</value></data>
  <data name="NoFoldersToMoveMessage" xml:space="preserve"><value>Create a folder first to move sheets into.</value></data>
  <data name="OK" xml:space="preserve"><value>OK</value></data>
  <data name="Folder" xml:space="preserve"><value>Folder</value></data>
  <data name="GoToPageTitle" xml:space="preserve"><value>Go to Page</value></data>
  <data name="GoToPageMessageFormat" xml:space="preserve"><value>Enter a page number (1-{0}):</value></data>
  <data name="BookmarksTitle" xml:space="preserve"><value>Bookmarks</value></data>
  <data name="AddBookmarkOption" xml:space="preserve"><value>Add Bookmark</value></data>
  <data name="BookmarkPageLabelFormat" xml:space="preserve"><value>Page {0}</value></data>
  <data name="BookmarkNamedPageLabelFormat" xml:space="preserve"><value>{0} (Page {1})</value></data>
  <data name="PageNumberLabel" xml:space="preserve"><value>Page number</value></data>
  <data name="NameOptionalLabel" xml:space="preserve"><value>Name (optional)</value></data>
  <data name="NamePlaceholderExample" xml:space="preserve"><value>e.g. Coda</value></data>
  <data name="Add" xml:space="preserve"><value>Add</value></data>
  <data name="MusicIcons" xml:space="preserve"><value>Music Icons</value></data>
  <data name="Pencil" xml:space="preserve"><value>Pencil</value></data>
  <data name="Color" xml:space="preserve"><value>Color</value></data>
  <data name="Width" xml:space="preserve"><value>Width</value></data>
  <data name="Eraser" xml:space="preserve"><value>Eraser</value></data>
  <data name="Size" xml:space="preserve"><value>Size</value></data>
  <data name="PageIndicatorFormat" xml:space="preserve"><value>Page {0} of {1}</value></data>
  <data name="CategoryDynamics" xml:space="preserve"><value>Dynamics</value></data>
  <data name="CategoryArticulations" xml:space="preserve"><value>Articulations</value></data>
  <data name="CategoryFermataBreath" xml:space="preserve"><value>Fermata &amp; Breath</value></data>
  <data name="CategoryHairpins" xml:space="preserve"><value>Hairpins</value></data>
  <data name="IconStaccato" xml:space="preserve"><value>Staccato</value></data>
  <data name="IconAccent" xml:space="preserve"><value>Accent</value></data>
  <data name="IconTenuto" xml:space="preserve"><value>Tenuto</value></data>
  <data name="IconMarcato" xml:space="preserve"><value>Marcato</value></data>
  <data name="IconFermata" xml:space="preserve"><value>Fermata</value></data>
  <data name="IconBreathMark" xml:space="preserve"><value>Breath mark</value></data>
  <data name="IconCrescendo" xml:space="preserve"><value>Crescendo</value></data>
  <data name="IconDecrescendo" xml:space="preserve"><value>Decrescendo</value></data>
  <data name="LanguageSectionHeader" xml:space="preserve"><value>Language</value></data>
  <data name="RestartRequiredTitle" xml:space="preserve"><value>Restart Required</value></data>
  <data name="RestartRequiredMessage" xml:space="preserve"><value>Restart PiccoloReader for the new language to take effect.</value></data>
</root>
```

- [ ] **Step 2: Create the Spanish resx file**

Create `src/PiccoloReader.Core/Resources/Strings/AppStrings.es.resx` — same schema header, same `name` attributes, translated values:

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
    <xsd:element name="root" msdata:IsDataSet="true">
      <xsd:complexType>
        <xsd:choice maxOccurs="unbounded">
          <xsd:element name="data">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" msdata:Ordinal="1" />
              <xsd:attribute name="type" type="xsd:string" msdata:Ordinal="3" />
              <xsd:attribute name="mimetype" type="xsd:string" msdata:Ordinal="4" />
            </xsd:complexType>
          </xsd:element>
        </xsd:choice>
      </xsd:complexType>
    </xsd:element>
  </xsd:schema>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <data name="Library" xml:space="preserve"><value>Biblioteca</value></data>
  <data name="Settings" xml:space="preserve"><value>Ajustes</value></data>
  <data name="MadeWithLoveBy" xml:space="preserve"><value>Hecho con amor por</value></data>
  <data name="Folders" xml:space="preserve"><value>Carpetas</value></data>
  <data name="Sheets" xml:space="preserve"><value>Partituras</value></data>
  <data name="SearchFoldersPlaceholder" xml:space="preserve"><value>Buscar carpetas</value></data>
  <data name="SearchSheetsPlaceholder" xml:space="preserve"><value>Buscar partituras</value></data>
  <data name="SelectPdfsPickerTitle" xml:space="preserve"><value>Seleccionar PDFs</value></data>
  <data name="CreateFolderTitle" xml:space="preserve"><value>Crear carpeta</value></data>
  <data name="FolderNamePrompt" xml:space="preserve"><value>Nombre de la carpeta:</value></data>
  <data name="SortFoldersByTitle" xml:space="preserve"><value>Ordenar carpetas por</value></data>
  <data name="SortSheetsByTitle" xml:space="preserve"><value>Ordenar partituras por</value></data>
  <data name="SortByTitle" xml:space="preserve"><value>Ordenar por</value></data>
  <data name="Cancel" xml:space="preserve"><value>Cancelar</value></data>
  <data name="SortNameAscending" xml:space="preserve"><value>Nombre (A-Z)</value></data>
  <data name="SortNameDescending" xml:space="preserve"><value>Nombre (Z-A)</value></data>
  <data name="SortDateAddedAscending" xml:space="preserve"><value>Fecha de adición (más antiguas primero)</value></data>
  <data name="SortDateAddedDescending" xml:space="preserve"><value>Fecha de adición (más recientes primero)</value></data>
  <data name="DeleteSheetsOption" xml:space="preserve"><value>Eliminar partituras</value></data>
  <data name="KeepSheetsOption" xml:space="preserve"><value>Conservar partituras</value></data>
  <data name="Move" xml:space="preserve"><value>Mover</value></data>
  <data name="Delete" xml:space="preserve"><value>Eliminar</value></data>
  <data name="DeleteSheetTitle" xml:space="preserve"><value>Eliminar partitura</value></data>
  <data name="DeleteSheetMessageFormat" xml:space="preserve"><value>¿Eliminar "{0}"? Esta acción no se puede deshacer.</value></data>
  <data name="RootOption" xml:space="preserve"><value>Raíz</value></data>
  <data name="MoveToTitle" xml:space="preserve"><value>Mover a…</value></data>
  <data name="NoFoldersToMoveMessage" xml:space="preserve"><value>Primero crea una carpeta para poder mover partituras a ella.</value></data>
  <data name="OK" xml:space="preserve"><value>Aceptar</value></data>
  <data name="Folder" xml:space="preserve"><value>Carpeta</value></data>
  <data name="GoToPageTitle" xml:space="preserve"><value>Ir a la página</value></data>
  <data name="GoToPageMessageFormat" xml:space="preserve"><value>Ingresa un número de página (1-{0}):</value></data>
  <data name="BookmarksTitle" xml:space="preserve"><value>Marcadores</value></data>
  <data name="AddBookmarkOption" xml:space="preserve"><value>Añadir marcador</value></data>
  <data name="BookmarkPageLabelFormat" xml:space="preserve"><value>Página {0}</value></data>
  <data name="BookmarkNamedPageLabelFormat" xml:space="preserve"><value>{0} (Página {1})</value></data>
  <data name="PageNumberLabel" xml:space="preserve"><value>Número de página</value></data>
  <data name="NameOptionalLabel" xml:space="preserve"><value>Nombre (opcional)</value></data>
  <data name="NamePlaceholderExample" xml:space="preserve"><value>p. ej. Coda</value></data>
  <data name="Add" xml:space="preserve"><value>Añadir</value></data>
  <data name="MusicIcons" xml:space="preserve"><value>Iconos musicales</value></data>
  <data name="Pencil" xml:space="preserve"><value>Lápiz</value></data>
  <data name="Color" xml:space="preserve"><value>Color</value></data>
  <data name="Width" xml:space="preserve"><value>Grosor</value></data>
  <data name="Eraser" xml:space="preserve"><value>Borrador</value></data>
  <data name="Size" xml:space="preserve"><value>Tamaño</value></data>
  <data name="PageIndicatorFormat" xml:space="preserve"><value>Página {0} de {1}</value></data>
  <data name="CategoryDynamics" xml:space="preserve"><value>Dinámica</value></data>
  <data name="CategoryArticulations" xml:space="preserve"><value>Articulaciones</value></data>
  <data name="CategoryFermataBreath" xml:space="preserve"><value>Calderón y respiración</value></data>
  <data name="CategoryHairpins" xml:space="preserve"><value>Reguladores</value></data>
  <data name="IconStaccato" xml:space="preserve"><value>Staccato</value></data>
  <data name="IconAccent" xml:space="preserve"><value>Acento</value></data>
  <data name="IconTenuto" xml:space="preserve"><value>Tenuto</value></data>
  <data name="IconMarcato" xml:space="preserve"><value>Marcato</value></data>
  <data name="IconFermata" xml:space="preserve"><value>Calderón</value></data>
  <data name="IconBreathMark" xml:space="preserve"><value>Marca de respiración</value></data>
  <data name="IconCrescendo" xml:space="preserve"><value>Crescendo</value></data>
  <data name="IconDecrescendo" xml:space="preserve"><value>Decrescendo</value></data>
  <data name="LanguageSectionHeader" xml:space="preserve"><value>Idioma</value></data>
  <data name="RestartRequiredTitle" xml:space="preserve"><value>Reinicio necesario</value></data>
  <data name="RestartRequiredMessage" xml:space="preserve"><value>Reinicia PiccoloReader para que el nuevo idioma tenga efecto.</value></data>
</root>
```

- [ ] **Step 3: Write the failing test for `AppStrings`**

Create `tests/PiccoloReader.Core.Tests/Resources/AppStringsTests.cs`:

```csharp
using System.Globalization;
using PiccoloReader.Core.Resources.Strings;

namespace PiccoloReader.Core.Tests.Resources;

public class AppStringsTests
{
    [Fact]
    public void Library_EnglishCulture_ReturnsEnglishValue()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            Assert.Equal("Library", AppStrings.Library);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void Library_SpanishCulture_ReturnsSpanishValue()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("es");
            Assert.Equal("Biblioteca", AppStrings.Library);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void SearchFoldersPlaceholder_SpanishCulture_ReturnsSpanishValue()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("es");
            Assert.Equal("Buscar carpetas", AppStrings.SearchFoldersPlaceholder);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void PageIndicatorFormat_UsedWithStringFormat_ProducesExpectedEnglishText()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            var text = string.Format(AppStrings.PageIndicatorFormat, 2, 5);
            Assert.Equal("Page 2 of 5", text);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter AppStringsTests
```

Expected: FAIL (compile error — `PiccoloReader.Core.Resources.Strings.AppStrings` doesn't exist yet)

- [ ] **Step 5: Implement `AppStrings`**

Create `src/PiccoloReader.Core/Resources/Strings/AppStrings.cs`:

```csharp
using System.Globalization;
using System.Resources;

namespace PiccoloReader.Core.Resources.Strings;

public static class AppStrings
{
    private static readonly ResourceManager ResourceManager =
        new("PiccoloReader.Core.Resources.Strings.AppStrings", typeof(AppStrings).Assembly);

    public static string Library => Get(nameof(Library));
    public static string Settings => Get(nameof(Settings));
    public static string MadeWithLoveBy => Get(nameof(MadeWithLoveBy));
    public static string Folders => Get(nameof(Folders));
    public static string Sheets => Get(nameof(Sheets));
    public static string SearchFoldersPlaceholder => Get(nameof(SearchFoldersPlaceholder));
    public static string SearchSheetsPlaceholder => Get(nameof(SearchSheetsPlaceholder));
    public static string SelectPdfsPickerTitle => Get(nameof(SelectPdfsPickerTitle));
    public static string CreateFolderTitle => Get(nameof(CreateFolderTitle));
    public static string FolderNamePrompt => Get(nameof(FolderNamePrompt));
    public static string SortFoldersByTitle => Get(nameof(SortFoldersByTitle));
    public static string SortSheetsByTitle => Get(nameof(SortSheetsByTitle));
    public static string SortByTitle => Get(nameof(SortByTitle));
    public static string Cancel => Get(nameof(Cancel));
    public static string SortNameAscending => Get(nameof(SortNameAscending));
    public static string SortNameDescending => Get(nameof(SortNameDescending));
    public static string SortDateAddedAscending => Get(nameof(SortDateAddedAscending));
    public static string SortDateAddedDescending => Get(nameof(SortDateAddedDescending));
    public static string DeleteSheetsOption => Get(nameof(DeleteSheetsOption));
    public static string KeepSheetsOption => Get(nameof(KeepSheetsOption));
    public static string Move => Get(nameof(Move));
    public static string Delete => Get(nameof(Delete));
    public static string DeleteSheetTitle => Get(nameof(DeleteSheetTitle));
    public static string DeleteSheetMessageFormat => Get(nameof(DeleteSheetMessageFormat));
    public static string RootOption => Get(nameof(RootOption));
    public static string MoveToTitle => Get(nameof(MoveToTitle));
    public static string NoFoldersToMoveMessage => Get(nameof(NoFoldersToMoveMessage));
    public static string OK => Get(nameof(OK));
    public static string Folder => Get(nameof(Folder));
    public static string GoToPageTitle => Get(nameof(GoToPageTitle));
    public static string GoToPageMessageFormat => Get(nameof(GoToPageMessageFormat));
    public static string BookmarksTitle => Get(nameof(BookmarksTitle));
    public static string AddBookmarkOption => Get(nameof(AddBookmarkOption));
    public static string BookmarkPageLabelFormat => Get(nameof(BookmarkPageLabelFormat));
    public static string BookmarkNamedPageLabelFormat => Get(nameof(BookmarkNamedPageLabelFormat));
    public static string PageNumberLabel => Get(nameof(PageNumberLabel));
    public static string NameOptionalLabel => Get(nameof(NameOptionalLabel));
    public static string NamePlaceholderExample => Get(nameof(NamePlaceholderExample));
    public static string Add => Get(nameof(Add));
    public static string MusicIcons => Get(nameof(MusicIcons));
    public static string Pencil => Get(nameof(Pencil));
    public static string Color => Get(nameof(Color));
    public static string Width => Get(nameof(Width));
    public static string Eraser => Get(nameof(Eraser));
    public static string Size => Get(nameof(Size));
    public static string PageIndicatorFormat => Get(nameof(PageIndicatorFormat));
    public static string CategoryDynamics => Get(nameof(CategoryDynamics));
    public static string CategoryArticulations => Get(nameof(CategoryArticulations));
    public static string CategoryFermataBreath => Get(nameof(CategoryFermataBreath));
    public static string CategoryHairpins => Get(nameof(CategoryHairpins));
    public static string IconStaccato => Get(nameof(IconStaccato));
    public static string IconAccent => Get(nameof(IconAccent));
    public static string IconTenuto => Get(nameof(IconTenuto));
    public static string IconMarcato => Get(nameof(IconMarcato));
    public static string IconFermata => Get(nameof(IconFermata));
    public static string IconBreathMark => Get(nameof(IconBreathMark));
    public static string IconCrescendo => Get(nameof(IconCrescendo));
    public static string IconDecrescendo => Get(nameof(IconDecrescendo));
    public static string LanguageSectionHeader => Get(nameof(LanguageSectionHeader));
    public static string RestartRequiredTitle => Get(nameof(RestartRequiredTitle));
    public static string RestartRequiredMessage => Get(nameof(RestartRequiredMessage));

    private static string Get(string key) =>
        ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter AppStringsTests
```

Expected: PASS (4 tests)

- [ ] **Step 7: Run the full Core test suite (regression check)**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj
```

Expected: PASS (93 tests — 89 existing + 4 new)

- [ ] **Step 8: Commit**

```bash
git add src/PiccoloReader.Core/Resources tests/PiccoloReader.Core.Tests/Resources
git commit -m "feat: add AppStrings localization infrastructure (English + Spanish)"
```

---

## Task 2: Language fallback resolver

**Files:**
- Create: `src/PiccoloReader.Core/Services/LanguageResolver.cs`
- Test: `tests/PiccoloReader.Core.Tests/Services/LanguageResolverTests.cs`

**Interfaces:**
- Produces: `PiccoloReader.Core.Services.LanguageResolver.ResolveLanguageCode(string? savedCode, string deviceCode) : string` — consumed by Task 3's `MauiProgram` startup wiring.

- [ ] **Step 1: Write the failing tests**

Create `tests/PiccoloReader.Core.Tests/Services/LanguageResolverTests.cs`:

```csharp
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests.Services;

public class LanguageResolverTests
{
    [Fact]
    public void ResolveLanguageCode_SavedCodePresent_ReturnsSavedCode()
    {
        var result = LanguageResolver.ResolveLanguageCode("es", "en");
        Assert.Equal("es", result);
    }

    [Fact]
    public void ResolveLanguageCode_NoSavedCode_DeviceIsSpanish_ReturnsSpanish()
    {
        var result = LanguageResolver.ResolveLanguageCode(null, "es");
        Assert.Equal("es", result);
    }

    [Fact]
    public void ResolveLanguageCode_NoSavedCode_DeviceIsEnglish_ReturnsEnglish()
    {
        var result = LanguageResolver.ResolveLanguageCode(null, "en");
        Assert.Equal("en", result);
    }

    [Fact]
    public void ResolveLanguageCode_NoSavedCode_DeviceIsUnsupportedLanguage_FallsBackToEnglish()
    {
        var result = LanguageResolver.ResolveLanguageCode(null, "fr");
        Assert.Equal("en", result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter LanguageResolverTests
```

Expected: FAIL (compile error — `LanguageResolver` doesn't exist yet)

- [ ] **Step 3: Implement `LanguageResolver`**

Create `src/PiccoloReader.Core/Services/LanguageResolver.cs`:

```csharp
namespace PiccoloReader.Core.Services;

public static class LanguageResolver
{
    public static string ResolveLanguageCode(string? savedCode, string deviceCode) =>
        savedCode ?? (deviceCode == "es" ? "es" : "en");
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter LanguageResolverTests
```

Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add src/PiccoloReader.Core/Services/LanguageResolver.cs tests/PiccoloReader.Core.Tests/Services/LanguageResolverTests.cs
git commit -m "feat: add LanguageResolver fallback logic"
```

---

## Task 3: `ILanguagePreferenceService` + startup culture wiring

**Files:**
- Create: `src/PiccoloReader.Core/Services/ILanguagePreferenceService.cs`
- Create: `src/PiccoloReader/Services/MauiLanguagePreferenceService.cs`
- Modify: `src/PiccoloReader/MauiProgram.cs`

**Interfaces:**
- Consumes: `LanguageResolver.ResolveLanguageCode` (Task 2)
- Produces: `ILanguagePreferenceService` (`GetSavedLanguageCode() : string?`, `SaveLanguageCode(string code) : void`), registered `AddSingleton` — consumed by Task 4's `SettingsViewModel`.

No Core unit tests for the Maui implementation itself (it's a thin wrapper over `Preferences`, same as `MauiAppStorageProvider` has no dedicated test) — verified by running the app.

- [ ] **Step 1: Create the interface**

Create `src/PiccoloReader.Core/Services/ILanguagePreferenceService.cs`:

```csharp
namespace PiccoloReader.Core.Services;

public interface ILanguagePreferenceService
{
    string? GetSavedLanguageCode();

    void SaveLanguageCode(string code);
}
```

- [ ] **Step 2: Create the Maui implementation**

Create `src/PiccoloReader/Services/MauiLanguagePreferenceService.cs`:

```csharp
using PiccoloReader.Core.Services;

namespace PiccoloReader.Services;

public class MauiLanguagePreferenceService : ILanguagePreferenceService
{
    private const string PreferenceKey = "AppLanguage";

    public string? GetSavedLanguageCode() =>
        Preferences.Default.Get(PreferenceKey, (string?)null);

    public void SaveLanguageCode(string code) =>
        Preferences.Default.Set(PreferenceKey, code);
}
```

- [ ] **Step 3: Wire startup culture resolution and DI registration in `MauiProgram.cs`**

Modify `src/PiccoloReader/MauiProgram.cs`. Add this `using` alongside the existing ones (`PiccoloReader.Core.Services` and `PiccoloReader.Services` are already imported there, so `LanguageResolver` and `MauiLanguagePreferenceService` need no further qualification):

```csharp
using System.Globalization;
```

Right after `var builder = MauiApp.CreateBuilder();` and its `.UseMauiApp<App>()...` chain (i.e. after the existing `#if DEBUG ... #endif` block and `Batteries_V2.Init();`, before the `AddSingleton<IAppStorageProvider, ...>()` line), insert:

```csharp
		var savedLanguageCode = Preferences.Default.Get("AppLanguage", (string?)null);
		var deviceLanguageCode = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
		var resolvedLanguageCode = LanguageResolver.ResolveLanguageCode(savedLanguageCode, deviceLanguageCode);

		if (savedLanguageCode is null)
		{
			Preferences.Default.Set("AppLanguage", resolvedLanguageCode);
		}

		var resolvedCulture = new CultureInfo(resolvedLanguageCode);
		CultureInfo.CurrentCulture = resolvedCulture;
		CultureInfo.CurrentUICulture = resolvedCulture;
		CultureInfo.DefaultThreadCurrentCulture = resolvedCulture;
		CultureInfo.DefaultThreadCurrentUICulture = resolvedCulture;
```

Then, alongside the existing `builder.Services.AddSingleton<IAppStorageProvider, MauiAppStorageProvider>();` line, add:

```csharp
		builder.Services.AddSingleton<ILanguagePreferenceService, MauiLanguagePreferenceService>();
```

- [ ] **Step 4: Build the app**

```bash
dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android
```

Expected: builds successfully.

- [ ] **Step 5: Run the full Core test suite (regression check)**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj
```

Expected: PASS (97 tests — no change in count from Task 2, this task added no Core tests)

- [ ] **Step 6: Commit**

```bash
git add src/PiccoloReader.Core/Services/ILanguagePreferenceService.cs src/PiccoloReader/Services/MauiLanguagePreferenceService.cs src/PiccoloReader/MauiProgram.cs
git commit -m "feat: resolve and persist app language at startup"
```

---

## Task 4: `SettingsViewModel`

**Files:**
- Create: `src/PiccoloReader.Core/ViewModels/SettingsViewModel.cs`
- Test: `tests/PiccoloReader.Core.Tests/ViewModels/SettingsViewModelTests.cs`
- Test helper: `tests/PiccoloReader.Core.Tests/FakeLanguagePreferenceService.cs`

**Interfaces:**
- Consumes: `ILanguagePreferenceService` (Task 3)
- Produces: `SettingsViewModel` — `string SelectedLanguageCode` (bindable), `IRelayCommand<string> SetLanguageCommand` (synchronous — `SaveLanguageCode` does no I/O awaiting, so this is a plain `[RelayCommand]`, not an async one) — consumed by Task 5's `SettingsPage`.

- [ ] **Step 1: Create the fake service test double**

Create `tests/PiccoloReader.Core.Tests/FakeLanguagePreferenceService.cs`:

```csharp
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests;

public class FakeLanguagePreferenceService : ILanguagePreferenceService
{
    public string? SavedCode { get; set; }

    public string? GetSavedLanguageCode() => SavedCode;

    public void SaveLanguageCode(string code) => SavedCode = code;
}
```

- [ ] **Step 2: Write the failing tests**

Create `tests/PiccoloReader.Core.Tests/ViewModels/SettingsViewModelTests.cs`:

```csharp
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests.ViewModels;

public class SettingsViewModelTests
{
    [Fact]
    public void Constructor_SavedCodeIsSpanish_SelectedLanguageCodeIsSpanish()
    {
        var fake = new FakeLanguagePreferenceService { SavedCode = "es" };

        var sut = new SettingsViewModel(fake);

        Assert.Equal("es", sut.SelectedLanguageCode);
    }

    [Fact]
    public void Constructor_NoSavedCode_DefaultsToEnglish()
    {
        var fake = new FakeLanguagePreferenceService { SavedCode = null };

        var sut = new SettingsViewModel(fake);

        Assert.Equal("en", sut.SelectedLanguageCode);
    }

    [Fact]
    public void SetLanguageCommand_SavesCodeAndUpdatesSelectedLanguageCode()
    {
        var fake = new FakeLanguagePreferenceService { SavedCode = "en" };
        var sut = new SettingsViewModel(fake);

        sut.SetLanguageCommand.Execute("es");

        Assert.Equal("es", fake.SavedCode);
        Assert.Equal("es", sut.SelectedLanguageCode);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter SettingsViewModelTests
```

Expected: FAIL (compile error — `SettingsViewModel` doesn't exist yet)

- [ ] **Step 4: Implement `SettingsViewModel`**

Create `src/PiccoloReader.Core/ViewModels/SettingsViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ILanguagePreferenceService _languagePreferenceService;

    [ObservableProperty]
    private string _selectedLanguageCode;

    public SettingsViewModel(ILanguagePreferenceService languagePreferenceService)
    {
        _languagePreferenceService = languagePreferenceService;
        _selectedLanguageCode = languagePreferenceService.GetSavedLanguageCode() ?? "en";
    }

    [RelayCommand]
    private void SetLanguage(string code)
    {
        _languagePreferenceService.SaveLanguageCode(code);
        SelectedLanguageCode = code;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter SettingsViewModelTests
```

Expected: PASS (3 tests)

- [ ] **Step 6: Run the full Core test suite (regression check)**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj
```

Expected: PASS (100 tests — 97 existing + 3 new)

- [ ] **Step 7: Commit**

```bash
git add src/PiccoloReader.Core/ViewModels/SettingsViewModel.cs tests/PiccoloReader.Core.Tests/ViewModels/SettingsViewModelTests.cs tests/PiccoloReader.Core.Tests/FakeLanguagePreferenceService.cs
git commit -m "feat: add SettingsViewModel"
```

---

## Task 5: `SettingsPage` + flyout entry

**Files:**
- Create: `src/PiccoloReader/Views/SettingsPage.xaml`
- Create: `src/PiccoloReader/Views/SettingsPage.xaml.cs`
- Modify: `src/PiccoloReader/AppShell.xaml`
- Modify: `src/PiccoloReader/MauiProgram.cs`

**Interfaces:**
- Consumes: `SettingsViewModel` (Task 4)

No Core unit tests — this is UI, verified by running the app.

- [ ] **Step 1: Create `SettingsPage.xaml`**

Create `src/PiccoloReader/Views/SettingsPage.xaml`:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ContentPage
    x:Class="PiccoloReader.Views.SettingsPage"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:strings="clr-namespace:PiccoloReader.Core.Resources.Strings;assembly=PiccoloReader.Core"
    Title="{x:Static strings:AppStrings.Settings}">

    <VerticalStackLayout Padding="16" Spacing="12">

        <Label Text="{x:Static strings:AppStrings.LanguageSectionHeader}" FontAttributes="Bold" FontSize="16" />

        <Border
            x:Name="EnglishRow"
            Style="{StaticResource ListItemCard}">
            <Border.GestureRecognizers>
                <TapGestureRecognizer Tapped="OnEnglishTapped" />
            </Border.GestureRecognizers>
            <Label Text="English" VerticalOptions="Center" FontSize="16" />
        </Border>

        <Border
            x:Name="SpanishRow"
            Style="{StaticResource ListItemCard}">
            <Border.GestureRecognizers>
                <TapGestureRecognizer Tapped="OnSpanishTapped" />
            </Border.GestureRecognizers>
            <Label Text="Español" VerticalOptions="Center" FontSize="16" />
        </Border>

    </VerticalStackLayout>
</ContentPage>
```

- [ ] **Step 2: Create `SettingsPage.xaml.cs`**

Create `src/PiccoloReader/Views/SettingsPage.xaml.cs`:

```csharp
using PiccoloReader.Core.Resources.Strings;
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateLanguageSelectionVisuals();
    }

    private async void OnEnglishTapped(object? sender, TappedEventArgs e)
    {
        await SelectLanguageAsync("en");
    }

    private async void OnSpanishTapped(object? sender, TappedEventArgs e)
    {
        await SelectLanguageAsync("es");
    }

    private async Task SelectLanguageAsync(string code)
    {
        if (_viewModel.SelectedLanguageCode == code)
        {
            return;
        }

        _viewModel.SetLanguageCommand.Execute(code);
        UpdateLanguageSelectionVisuals();

        await DisplayAlertAsync(AppStrings.RestartRequiredTitle, AppStrings.RestartRequiredMessage, AppStrings.OK);
    }

    // Mirrors SheetViewerPage's SetColorRingSelected pattern: the selected
    // row gets a colored stroke + a small drop shadow, the other reverts
    // to the plain ListItemCard style - same selection convention already
    // used elsewhere in this app, no new converter/binding machinery.
    private void UpdateLanguageSelectionVisuals()
    {
        SetRowSelected(EnglishRow, _viewModel.SelectedLanguageCode == "en");
        SetRowSelected(SpanishRow, _viewModel.SelectedLanguageCode == "es");
    }

    private void SetRowSelected(Border row, bool isSelected)
    {
        row.StrokeThickness = isSelected ? 2 : 1;
        row.Stroke = isSelected
            ? (Color)Application.Current!.Resources["Primary"]
            : (Color)Application.Current!.Resources["Gray200"];
        row.Shadow = isSelected
            ? new Shadow { Brush = Colors.Black, Opacity = 0.3f, Radius = 6, Offset = new Point(0, 2) }
            : null!;
    }
}
```

- [ ] **Step 3: Add the Settings `FlyoutItem` to `AppShell.xaml`**

Modify `src/PiccoloReader/AppShell.xaml`. Immediately after the closing `</FlyoutItem>` tag of the existing "Library" item (before `<Shell.FlyoutFooter>`), insert:

```xml
    <FlyoutItem Title="{x:Static strings:AppStrings.Settings}" Route="settings">
        <FlyoutItem.FlyoutIcon>
            <FontImageSource
                Glyph="&#xE429;"
                FontFamily="MaterialOutlined"
                Size="24"
                Color="{AppThemeBinding Light={StaticResource Primary}, Dark={StaticResource PrimaryDark}}" />
        </FlyoutItem.FlyoutIcon>
        <ShellContent ContentTemplate="{DataTemplate views:SettingsPage}" />
    </FlyoutItem>
```

Also change the existing "Library" `FlyoutItem`'s `Title="Library"` to `Title="{x:Static strings:AppStrings.Library}"`, and add this namespace declaration to the `<Shell ...>` root element's attribute list (alongside the existing `xmlns:views="clr-namespace:PiccoloReader.Views"`):

```xml
    xmlns:strings="clr-namespace:PiccoloReader.Core.Resources.Strings;assembly=PiccoloReader.Core"
```

- [ ] **Step 4: Register `SettingsPage`/`SettingsViewModel` in `MauiProgram.cs`**

Modify `src/PiccoloReader/MauiProgram.cs`. Alongside the existing `builder.Services.AddTransient<SheetViewerViewModel>();` and `builder.Services.AddTransient<SheetViewerPage>();` lines, add:

```csharp
		builder.Services.AddTransient<SettingsViewModel>();
```

and

```csharp
		builder.Services.AddTransient<SettingsPage>();
```

(`PiccoloReader.Core.ViewModels` and `PiccoloReader.Views` are already `using`d in this file for the existing ViewModels/Pages, so no new `using` is needed for these two types.)

- [ ] **Step 5: Build the app**

```bash
dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android
```

Expected: builds successfully.

- [ ] **Step 6: Manual verification**

Deploy to the emulator (`dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android -t:Run` — add `"-p:AdbTarget=-s <serial>"` if more than one device is attached). Verify:
- Flyout shows "Settings" below "Library", with the tune/gear icon.
- Tapping it opens the Settings page, showing "Language" then "English" and "Español" rows.
- The row matching the current language has a highlighted (colored, shadowed) border.
- Tapping the other language shows "Restart Required" / the restart message, and the tapped row becomes the highlighted one.
- Tapping the already-selected row does nothing (no dialog, per `SelectLanguageAsync`'s early-return).

- [ ] **Step 7: Run the full Core test suite (regression check)**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj
```

Expected: PASS (100 tests — this task touched no Core code)

- [ ] **Step 8: Commit**

```bash
git add src/PiccoloReader/Views/SettingsPage.xaml src/PiccoloReader/Views/SettingsPage.xaml.cs src/PiccoloReader/AppShell.xaml src/PiccoloReader/MauiProgram.cs
git commit -m "feat: add Settings page and flyout entry"
```

---

## Task 6: Localize `AppShell.xaml` footer and `LibraryPage`

**Files:**
- Modify: `src/PiccoloReader/AppShell.xaml`
- Modify: `src/PiccoloReader/Views/LibraryPage.xaml`
- Modify: `src/PiccoloReader/Views/LibraryPage.xaml.cs`

No Core unit tests — this is UI, verified by running the app.

- [ ] **Step 1: Localize the flyout footer in `AppShell.xaml`**

Modify `src/PiccoloReader/AppShell.xaml`. Change:

```xml
            <Label
                Text="Made with love by"
```

to:

```xml
            <Label
                Text="{x:Static strings:AppStrings.MadeWithLoveBy}"
```

(the `strings` namespace was already added in Task 5, Step 3).

- [ ] **Step 2: Localize `LibraryPage.xaml`**

Modify `src/PiccoloReader/Views/LibraryPage.xaml`. Add this namespace to the `<ContentPage ...>` attribute list:

```xml
    xmlns:strings="clr-namespace:PiccoloReader.Core.Resources.Strings;assembly=PiccoloReader.Core"
```

Change `Title="Library"` to `Title="{x:Static strings:AppStrings.Library}"`.

Change `<Label Grid.Column="0" Text="Folders" FontAttributes="Bold" VerticalOptions="Center" />` to `<Label Grid.Column="0" Text="{x:Static strings:AppStrings.Folders}" FontAttributes="Bold" VerticalOptions="Center" />`.

Change `<Label Grid.Column="0" Text="Sheets" FontAttributes="Bold" VerticalOptions="Center" />` to `<Label Grid.Column="0" Text="{x:Static strings:AppStrings.Sheets}" FontAttributes="Bold" VerticalOptions="Center" />`.

Change `Placeholder="Search folders"` to `Placeholder="{x:Static strings:AppStrings.SearchFoldersPlaceholder}"`.

Change `Placeholder="Search sheets"` to `Placeholder="{x:Static strings:AppStrings.SearchSheetsPlaceholder}"`.

- [ ] **Step 3: Localize `LibraryPage.xaml.cs`**

Modify `src/PiccoloReader/Views/LibraryPage.xaml.cs`. Add `using PiccoloReader.Core.Resources.Strings;` to the `using`s.

In `OnImportPdfClicked`, change:

```csharp
            PickerTitle = "Select PDFs",
```

to:

```csharp
            PickerTitle = AppStrings.SelectPdfsPickerTitle,
```

In `OnCreateFolderClicked`, change:

```csharp
        var name = await DisplayPromptAsync("Create Folder", "Folder name:");
```

to:

```csharp
        var name = await DisplayPromptAsync(AppStrings.CreateFolderTitle, AppStrings.FolderNamePrompt);
```

In `OnFolderSortClicked`, change:

```csharp
        var choice = await DisplayActionSheetAsync(
            "Sort folders by",
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
```

to:

```csharp
        var choice = await DisplayActionSheetAsync(
            AppStrings.SortFoldersByTitle,
            AppStrings.Cancel,
            null,
            AppStrings.SortNameAscending,
            AppStrings.SortNameDescending,
            AppStrings.SortDateAddedAscending,
            AppStrings.SortDateAddedDescending);

        (SortField Field, SortDirection Direction)? sort = choice switch
        {
            var c when c == AppStrings.SortNameAscending => (SortField.Name, SortDirection.Ascending),
            var c when c == AppStrings.SortNameDescending => (SortField.Name, SortDirection.Descending),
            var c when c == AppStrings.SortDateAddedAscending => (SortField.DateAdded, SortDirection.Ascending),
            var c when c == AppStrings.SortDateAddedDescending => (SortField.DateAdded, SortDirection.Descending),
            _ => null
        };
```

In `OnSheetSortClicked`, change:

```csharp
        var choice = await DisplayActionSheetAsync(
            "Sort sheets by",
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
```

to:

```csharp
        var choice = await DisplayActionSheetAsync(
            AppStrings.SortSheetsByTitle,
            AppStrings.Cancel,
            null,
            AppStrings.SortNameAscending,
            AppStrings.SortNameDescending,
            AppStrings.SortDateAddedAscending,
            AppStrings.SortDateAddedDescending);

        (SortField Field, SortDirection Direction)? sort = choice switch
        {
            var c when c == AppStrings.SortNameAscending => (SortField.Name, SortDirection.Ascending),
            var c when c == AppStrings.SortNameDescending => (SortField.Name, SortDirection.Descending),
            var c when c == AppStrings.SortDateAddedAscending => (SortField.DateAdded, SortDirection.Ascending),
            var c when c == AppStrings.SortDateAddedDescending => (SortField.DateAdded, SortDirection.Descending),
            _ => null
        };
```

(unchanged logic below this — still calls `_viewModel.ApplySheetSort(selected.Field, selected.Direction)`, only the string literals feeding `choice` change.)

In `OnFolderLongPressedAsync`, change:

```csharp
        var choice = await DisplayActionSheetAsync($"\"{folder.Name}\"", "Cancel", null, "Delete Sheets", "Keep Sheets");

        switch (choice)
        {
            case "Delete Sheets":
                await _viewModel.DeleteFolderCommand.ExecuteAsync(folder);
                break;
            case "Keep Sheets":
                await _viewModel.DeleteFolderKeepSheetsCommand.ExecuteAsync(folder);
                break;
        }
```

to:

```csharp
        var choice = await DisplayActionSheetAsync($"\"{folder.Name}\"", AppStrings.Cancel, null, AppStrings.DeleteSheetsOption, AppStrings.KeepSheetsOption);

        if (choice == AppStrings.DeleteSheetsOption)
        {
            await _viewModel.DeleteFolderCommand.ExecuteAsync(folder);
        }
        else if (choice == AppStrings.KeepSheetsOption)
        {
            await _viewModel.DeleteFolderKeepSheetsCommand.ExecuteAsync(folder);
        }
```

In `OnSheetLongPressedAsync`, change:

```csharp
        var choice = await DisplayActionSheetAsync($"\"{sheet.Title}\"", "Cancel", null, "Move", "Delete");

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

            var target = await DisplayActionSheetAsync("Move to…", "Cancel", null, folders.Select(f => f.Name).ToArray());

            if (target is null || target == "Cancel")
            {
                return;
            }

            var targetFolder = folders.First(f => f.Name == target);
            await _viewModel.MoveSheetCommand.ExecuteAsync((sheet, (int?)targetFolder.Id));
        }
```

to:

```csharp
        var choice = await DisplayActionSheetAsync($"\"{sheet.Title}\"", AppStrings.Cancel, null, AppStrings.Move, AppStrings.Delete);

        if (choice == AppStrings.Delete)
        {
            var confirmed = await DisplayAlertAsync(
                AppStrings.DeleteSheetTitle,
                string.Format(AppStrings.DeleteSheetMessageFormat, sheet.Title),
                AppStrings.Delete,
                AppStrings.Cancel);

            if (confirmed)
            {
                await _viewModel.DeleteSheetCommand.ExecuteAsync(sheet);
            }
        }
        else if (choice == AppStrings.Move)
        {
            var folders = await _libraryService.GetFoldersAsync();

            if (folders.Count == 0)
            {
                await DisplayAlertAsync(AppStrings.Move, AppStrings.NoFoldersToMoveMessage, AppStrings.OK);
                return;
            }

            var target = await DisplayActionSheetAsync(AppStrings.MoveToTitle, AppStrings.Cancel, null, folders.Select(f => f.Name).ToArray());

            if (target is null || target == AppStrings.Cancel)
            {
                return;
            }

            var targetFolder = folders.First(f => f.Name == target);
            await _viewModel.MoveSheetCommand.ExecuteAsync((sheet, (int?)targetFolder.Id));
        }
```

- [ ] **Step 4: Build the app**

```bash
dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android
```

Expected: builds successfully.

- [ ] **Step 5: Manual verification**

Run the app on the emulator. Verify the Library page's title, section headers, search placeholders, and every dialog/action-sheet listed above still work exactly as before (English, since the device/emulator locale is English) — this task only changes *where the strings come from*, not behavior.

- [ ] **Step 6: Run the full Core test suite (regression check)**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj
```

Expected: PASS (100 tests — this task touched no Core code)

- [ ] **Step 7: Commit**

```bash
git add src/PiccoloReader/AppShell.xaml src/PiccoloReader/Views/LibraryPage.xaml src/PiccoloReader/Views/LibraryPage.xaml.cs
git commit -m "feat: localize AppShell footer and Library page"
```

---

## Task 7: Localize `FolderPage`

**Files:**
- Modify: `src/PiccoloReader/Views/FolderPage.xaml`
- Modify: `src/PiccoloReader/Views/FolderPage.xaml.cs`

No Core unit tests — this is UI, verified by running the app.

- [ ] **Step 1: Localize `FolderPage.xaml`**

Modify `src/PiccoloReader/Views/FolderPage.xaml`. Add the same `xmlns:strings="clr-namespace:PiccoloReader.Core.Resources.Strings;assembly=PiccoloReader.Core"` to the `<ContentPage ...>` attribute list.

Change `Title="Folder"` to `Title="{x:Static strings:AppStrings.Folder}"` (this is immediately overwritten by the `FolderName` query property in practice, but stays correct as a fallback default).

Change `Placeholder="Search sheets"` to `Placeholder="{x:Static strings:AppStrings.SearchSheetsPlaceholder}"`.

- [ ] **Step 2: Localize `FolderPage.xaml.cs`**

Modify `src/PiccoloReader/Views/FolderPage.xaml.cs`. Add `using PiccoloReader.Core.Resources.Strings;` to the `using`s.

In `OnImportPdfClicked`, change `PickerTitle = "Select PDFs",` to `PickerTitle = AppStrings.SelectPdfsPickerTitle,`.

In `OnSortClicked`, change:

```csharp
        var choice = await DisplayActionSheetAsync(
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
```

to:

```csharp
        var choice = await DisplayActionSheetAsync(
            AppStrings.SortByTitle,
            AppStrings.Cancel,
            null,
            AppStrings.SortNameAscending,
            AppStrings.SortNameDescending,
            AppStrings.SortDateAddedAscending,
            AppStrings.SortDateAddedDescending);

        (SortField Field, SortDirection Direction)? sort = choice switch
        {
            var c when c == AppStrings.SortNameAscending => (SortField.Name, SortDirection.Ascending),
            var c when c == AppStrings.SortNameDescending => (SortField.Name, SortDirection.Descending),
            var c when c == AppStrings.SortDateAddedAscending => (SortField.DateAdded, SortDirection.Ascending),
            var c when c == AppStrings.SortDateAddedDescending => (SortField.DateAdded, SortDirection.Descending),
            _ => null
        };
```

In `OnSheetLongPressedAsync`, change:

```csharp
        var choice = await DisplayActionSheetAsync($"\"{sheet.Title}\"", "Cancel", null, "Move", "Delete");

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

            var target = await DisplayActionSheetAsync("Move to…", "Cancel", null, options.ToArray());

            if (target is null || target == "Cancel")
            {
                return;
            }

            int? targetFolderId = target == "Root"
                ? null
                : folders.First(f => f.Name == target).Id;

            await _viewModel.MoveSheetCommand.ExecuteAsync((sheet, targetFolderId));
        }
```

to:

```csharp
        var choice = await DisplayActionSheetAsync($"\"{sheet.Title}\"", AppStrings.Cancel, null, AppStrings.Move, AppStrings.Delete);

        if (choice == AppStrings.Delete)
        {
            var confirmed = await DisplayAlertAsync(
                AppStrings.DeleteSheetTitle,
                string.Format(AppStrings.DeleteSheetMessageFormat, sheet.Title),
                AppStrings.Delete,
                AppStrings.Cancel);

            if (confirmed)
            {
                await _viewModel.DeleteSheetCommand.ExecuteAsync(sheet);
            }
        }
        else if (choice == AppStrings.Move)
        {
            var folders = await _libraryService.GetFoldersAsync();

            var options = new List<string> { AppStrings.RootOption };
            options.AddRange(folders.Where(f => f.Id != _viewModel.FolderId).Select(f => f.Name));

            var target = await DisplayActionSheetAsync(AppStrings.MoveToTitle, AppStrings.Cancel, null, options.ToArray());

            if (target is null || target == AppStrings.Cancel)
            {
                return;
            }

            int? targetFolderId = target == AppStrings.RootOption
                ? null
                : folders.First(f => f.Name == target).Id;

            await _viewModel.MoveSheetCommand.ExecuteAsync((sheet, targetFolderId));
        }
```

- [ ] **Step 3: Build the app**

```bash
dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android
```

Expected: builds successfully.

- [ ] **Step 4: Manual verification**

Run the app, open a folder, verify Import/Sort/Move/Delete all still behave exactly as before.

- [ ] **Step 5: Run the full Core test suite (regression check)**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj
```

Expected: PASS (100 tests — this task touched no Core code)

- [ ] **Step 6: Commit**

```bash
git add src/PiccoloReader/Views/FolderPage.xaml src/PiccoloReader/Views/FolderPage.xaml.cs
git commit -m "feat: localize Folder page"
```

---

## Task 8: Localize `SheetViewerPage` and `SheetViewerViewModel`

**Files:**
- Modify: `src/PiccoloReader.Core/ViewModels/SheetViewerViewModel.cs`
- Modify: `src/PiccoloReader/Views/SheetViewerPage.xaml`
- Modify: `src/PiccoloReader/Views/SheetViewerPage.xaml.cs`
- Test: `tests/PiccoloReader.Core.Tests/ViewModels/SheetViewerViewModelTests.cs` (existing file — add one test)

**Interfaces:**
- Consumes: `AppStrings.PageIndicatorFormat` (Task 1)

- [ ] **Step 1: Write the failing test for the localized page indicator**

Add this test method to the existing `tests/PiccoloReader.Core.Tests/ViewModels/SheetViewerViewModelTests.cs`, inside the existing `SheetViewerViewModelTests` class (which already constructs a ready-to-use `_sut` in its constructor — no additional setup needed, since this test only checks which language `PageIndicatorText` renders in, not any particular page/count values):

```csharp
    [Fact]
    public void PageIndicatorText_SpanishCulture_UsesSpanishFormat()
    {
        var original = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("es");
            Assert.Contains("Página", _sut.PageIndicatorText);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = original;
        }
    }
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter PageIndicatorText_SpanishCulture_UsesSpanishFormat
```

Expected: FAIL (current implementation is a hardcoded English `$"Page ..."` string, contains no "Página")

- [ ] **Step 3: Localize `PageIndicatorText` in `SheetViewerViewModel.cs`**

Modify `src/PiccoloReader.Core/ViewModels/SheetViewerViewModel.cs`. Add `using PiccoloReader.Core.Resources.Strings;` to the `using`s. Change:

```csharp
    public string PageIndicatorText => $"Page {CurrentPageDisplay} of {PageCount}";
```

to:

```csharp
    public string PageIndicatorText => string.Format(AppStrings.PageIndicatorFormat, CurrentPageDisplay, PageCount);
```

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter PageIndicatorText_SpanishCulture_UsesSpanishFormat
```

Expected: PASS

- [ ] **Step 5: Localize `SheetViewerPage.xaml`**

Modify `src/PiccoloReader/Views/SheetViewerPage.xaml`. Add the same `xmlns:strings="clr-namespace:PiccoloReader.Core.Resources.Strings;assembly=PiccoloReader.Core"` to the `<ContentPage ...>` attribute list.

In the `ToolPanel`'s content, change:
- `<Label Text="Music Icons" FontAttributes="Bold" FontSize="16" Margin="0,0,0,8" />` → `<Label Text="{x:Static strings:AppStrings.MusicIcons}" FontAttributes="Bold" FontSize="16" Margin="0,0,0,8" />`
- `<Label Text="Pencil" FontAttributes="Bold" FontSize="16" />` → `<Label Text="{x:Static strings:AppStrings.Pencil}" FontAttributes="Bold" FontSize="16" />`
- `<Label Text="Color" FontSize="14" />` → `<Label Text="{x:Static strings:AppStrings.Color}" FontSize="14" />`
- `<Label Text="Width" FontSize="14" />` → `<Label Text="{x:Static strings:AppStrings.Width}" FontSize="14" />`
- `<Label Text="Eraser" FontAttributes="Bold" FontSize="16" />` → `<Label Text="{x:Static strings:AppStrings.Eraser}" FontAttributes="Bold" FontSize="16" />`
- `<Label Text="Size" FontSize="14" />` → `<Label Text="{x:Static strings:AppStrings.Size}" FontSize="14" />`

In the `AddBookmarkOverlay`, change:
- `<Label Text="Add Bookmark" FontAttributes="Bold" FontSize="18" />` → `<Label Text="{x:Static strings:AppStrings.AddBookmarkOption}" FontAttributes="Bold" FontSize="18" />`
- `<Label Text="Page number" FontSize="14" />` → `<Label Text="{x:Static strings:AppStrings.PageNumberLabel}" FontSize="14" />`
- `<Label Text="Name (optional)" FontSize="14" />` → `<Label Text="{x:Static strings:AppStrings.NameOptionalLabel}" FontSize="14" />`
- `Placeholder="e.g. Coda"` → `Placeholder="{x:Static strings:AppStrings.NamePlaceholderExample}"`
- `<Button Text="Cancel" ... />` → `<Button Text="{x:Static strings:AppStrings.Cancel}" ... />`
- `<Button Text="Add" ... />` → `<Button Text="{x:Static strings:AppStrings.Add}" ... />`

- [ ] **Step 6: Localize `SheetViewerPage.xaml.cs`**

Modify `src/PiccoloReader/Views/SheetViewerPage.xaml.cs`. Add `using PiccoloReader.Core.Resources.Strings;` to the `using`s.

In `OnPageIndicatorTapped`, change:

```csharp
        var input = await DisplayPromptAsync(
            "Go to Page",
            $"Enter a page number (1-{_viewModel.PageCount}):",
            initialValue: _viewModel.CurrentPageDisplay.ToString(),
            keyboard: Keyboard.Numeric);
```

to:

```csharp
        var input = await DisplayPromptAsync(
            AppStrings.GoToPageTitle,
            string.Format(AppStrings.GoToPageMessageFormat, _viewModel.PageCount),
            initialValue: _viewModel.CurrentPageDisplay.ToString(),
            keyboard: Keyboard.Numeric);
```

In `OnBookmarksClicked`, change:

```csharp
        const string addBookmarkOption = "Add Bookmark";

        var ordered = _viewModel.Bookmarks.OrderBy(b => b.PageIndex).ToList();
        var options = ordered.Select(BookmarkOptionLabel).Append(addBookmarkOption).ToArray();

        var choice = await DisplayActionSheetAsync("Bookmarks", "Cancel", null, options);

        if (choice is null || choice == "Cancel")
        {
            return;
        }

        if (choice == addBookmarkOption)
```

to:

```csharp
        var addBookmarkOption = AppStrings.AddBookmarkOption;

        var ordered = _viewModel.Bookmarks.OrderBy(b => b.PageIndex).ToList();
        var options = ordered.Select(BookmarkOptionLabel).Append(addBookmarkOption).ToArray();

        var choice = await DisplayActionSheetAsync(AppStrings.BookmarksTitle, AppStrings.Cancel, null, options);

        if (choice is null || choice == AppStrings.Cancel)
        {
            return;
        }

        if (choice == addBookmarkOption)
```

Change `BookmarkOptionLabel`:

```csharp
    private static string BookmarkOptionLabel(Bookmark bookmark) =>
        string.IsNullOrWhiteSpace(bookmark.Name)
            ? $"Page {bookmark.PageIndex + 1}"
            : $"{bookmark.Name} (Page {bookmark.PageIndex + 1})";
```

to:

```csharp
    private static string BookmarkOptionLabel(Bookmark bookmark) =>
        string.IsNullOrWhiteSpace(bookmark.Name)
            ? string.Format(AppStrings.BookmarkPageLabelFormat, bookmark.PageIndex + 1)
            : string.Format(AppStrings.BookmarkNamedPageLabelFormat, bookmark.Name, bookmark.PageIndex + 1);
```

- [ ] **Step 7: Build the app**

```bash
dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android
```

Expected: builds successfully.

- [ ] **Step 8: Manual verification**

Run the app, open a sheet, verify the page indicator, bookmarks menu, add-bookmark dialog, and the Pencil/Eraser/Music Icons sidebar section labels all still work exactly as before.

- [ ] **Step 9: Run the full Core test suite (regression check)**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj
```

Expected: PASS (101 tests — 100 existing + 1 new)

- [ ] **Step 10: Commit**

```bash
git add src/PiccoloReader.Core/ViewModels/SheetViewerViewModel.cs tests/PiccoloReader.Core.Tests/ViewModels/SheetViewerViewModelTests.cs src/PiccoloReader/Views/SheetViewerPage.xaml src/PiccoloReader/Views/SheetViewerPage.xaml.cs
git commit -m "feat: localize Sheet Viewer page"
```

---

## Task 9: Localize `MusicIconCatalog`

**Files:**
- Modify: `src/PiccoloReader.Core/Services/MusicIconCatalog.cs`

**Interfaces:**
- Consumes: `AppStrings.CategoryDynamics`, `CategoryArticulations`, `CategoryFermataBreath`, `CategoryHairpins`, `IconStaccato`, `IconAccent`, `IconTenuto`, `IconMarcato`, `IconFermata`, `IconBreathMark`, `IconCrescendo`, `IconDecrescendo` (Task 1)

No unit tests for this file specifically (it has none today — `Categories`/`FindByKey` are static catalog data, exercised indirectly by whatever consumes them). Verified by running the app.

**`Categories` is a `static` property initialized once at type-load time** — since `AppStrings.*` reads `CultureInfo.CurrentUICulture` and the culture is resolved once at app startup (Task 3) *before* this type is ever touched, the category/icon names are correctly localized for the whole app session. (They would *not* update if the culture changed mid-session without a restart — consistent with the restart-required design.)

- [ ] **Step 1: Localize `MusicIconCatalog.cs`**

Modify `src/PiccoloReader.Core/Services/MusicIconCatalog.cs`. Add `using PiccoloReader.Core.Resources.Strings;` to the top. Replace the `Categories` initializer:

```csharp
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
            // Staccato's glyph is a small dot with tight ink bounds, so fitting
            // it to the same box as every other icon (DrawGlyphFitted scales
            // each glyph's own ink to nearly fill its target rect) blows it up
            // far larger than the dot is meant to read - VisualScale reins
            // that back in without affecting how any other icon renders.
            new("articStaccatoAbove", "Staccato", 0xE4A2, 1.00, 0.4),
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
```

with:

```csharp
    public static IReadOnlyList<MusicIconCategory> Categories { get; } = new List<MusicIconCategory>
    {
        // pp/p/f/ff/fp are standard musical dynamics notation - identical
        // in every language, not translated (see AppStrings.cs for every
        // other name in this catalog, which are genuine words).
        new(AppStrings.CategoryDynamics, new List<MusicIcon>
        {
            new("dynamicPP", "pp", 0xE52B, 1.95),
            new("dynamicPiano", "p", 0xE520, 1.09),
            new("dynamicForte", "f", 0xE522, 0.85),
            new("dynamicFF", "ff", 0xE52F, 1.25),
            new("dynamicFortePiano", "fp", 0xE534, 1.28),
        }),
        new(AppStrings.CategoryArticulations, new List<MusicIcon>
        {
            // Staccato's glyph is a small dot with tight ink bounds, so fitting
            // it to the same box as every other icon (DrawGlyphFitted scales
            // each glyph's own ink to nearly fill its target rect) blows it up
            // far larger than the dot is meant to read - VisualScale reins
            // that back in without affecting how any other icon renders.
            new("articStaccatoAbove", AppStrings.IconStaccato, 0xE4A2, 1.00, 0.4),
            new("articAccentAbove", AppStrings.IconAccent, 0xE4A0, 1.39),
            new("articTenutoAbove", AppStrings.IconTenuto, 0xE4A4, 7.06),
            new("articMarcatoAbove", AppStrings.IconMarcato, 0xE4AC, 0.93),
        }),
        new(AppStrings.CategoryFermataBreath, new List<MusicIcon>
        {
            new("fermataAbove", AppStrings.IconFermata, 0xE4C0, 1.81),
            new("breathMarkComma", AppStrings.IconBreathMark, 0xE4CE, 0.61),
        }),
        new(AppStrings.CategoryHairpins, new List<MusicIcon>
        {
            new("dynamicCrescendoHairpin", AppStrings.IconCrescendo, 0xE53E, 2.78),
            new("dynamicDiminuendoHairpin", AppStrings.IconDecrescendo, 0xE53F, 2.78),
        }),
    };
```

- [ ] **Step 2: Build the app**

```bash
dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android
```

Expected: builds successfully.

- [ ] **Step 3: Run the full Core test suite (regression check)**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj
```

Expected: PASS (101 tests — this task touched no test-covered behavior)

- [ ] **Step 4: Commit**

```bash
git add src/PiccoloReader.Core/Services/MusicIconCatalog.cs
git commit -m "feat: localize MusicIconCatalog category and icon names"
```

---

## Task 10: Full bilingual manual verification

**Files:** none (verification-only task)

No code changes. This task exists to catch anything the mechanical string-by-string tasks above could still get wrong — a missed hardcoded string, a typo'd `AppStrings` property name that silently falls back to the key itself, or a first-launch device-locale-detection bug — none of which a unit test can catch for the UI-facing parts.

- [ ] **Step 1: Build and deploy to the emulator**

```bash
dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android -t:Run
```

(add `"-p:AdbTarget=-s <serial>"` if more than one device/emulator is attached — see the `project_android_env` notes on Fast Deploy with multiple devices)

- [ ] **Step 2: Verify English (default) end-to-end**

Walk every page — Library (folders, sheets, search, sort, create folder, import), a Folder (sheets, search, sort, move, import), Sheet Viewer (page indicator, bookmarks, add bookmark, Music Icons/Pencil/Eraser FABs and their sidebar sections), and the flyout (Library, Settings, footer credit) — confirm every string reads in English and nothing shows a raw key name (e.g. literally the text "SearchFoldersPlaceholder" appearing on screen would mean a wiring bug).

- [ ] **Step 3: Switch to Spanish via the Settings page and restart**

In the app, Flyout → Settings → tap "Español". Confirm the "Reinicio necesario" dialog appears (in English still, since the culture hasn't changed yet this session). Force-stop and relaunch the app:

```bash
adb shell am force-stop com.fsoftt.piccoloreader
adb shell am start -n com.fsoftt.piccoloreader/crc64e68b3bc50f9f719f.MainActivity
```

- [ ] **Step 4: Verify Spanish end-to-end**

Repeat the same full walkthrough as Step 2, now in Spanish — confirm every string from the resx table above shows its Spanish translation, no key names leak through, and the Settings page itself now shows "Ajustes" in the flyout with the "Español" row highlighted.

- [ ] **Step 5: Switch back to English and verify the first-launch fallback separately**

In Settings, tap "English", restart, confirm everything is back to English. Then verify the first-launch device-locale fallback specifically, since it can't be exercised by just using the Settings page: clear the app's data (`adb shell pm clear com.fsoftt.piccoloreader`), change the emulator's system language to Spanish (Android Settings → System → Languages), relaunch the app fresh, and confirm it starts in Spanish *without ever touching the Settings page* — this is `LanguageResolver.ResolveLanguageCode`'s device-fallback path running for real, not the unit-tested pure function in isolation. Afterward, change the emulator's system language back to English and clear the app's data again, so the environment is left as it was found.

- [ ] **Step 6: Final regression check**

```bash
dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj
```

Expected: PASS (101 tests)

- [ ] **Step 7: Open a PR**

```bash
git push -u origin feature/multi-language-support
gh pr create --title "feat: add multi-language support (English + Spanish)" --body "$(cat <<'EOF'
## Summary
- Adds English + Spanish localization for every user-facing string in the app (all four pages, the flyout, and the Music Icons catalog), via a hand-written `AppStrings` class over two `.resx` files in `PiccoloReader.Core` (no IDE-generated code, CLI-build-safe).
- Adds a "Settings" flyout item and Settings page where the language can be changed. Language preference persists via `Preferences`; switching requires an app restart to take effect (confirmed acceptable — no live/reactive rebinding).
- First launch (no saved preference) defaults to Spanish if the device's system language is Spanish, English otherwise.

## Test plan
- [x] `dotnet build -f net10.0-android` succeeds
- [x] `dotnet test tests/PiccoloReader.Core.Tests` — 101/101 passing (12 new: AppStrings, LanguageResolver, SettingsViewModel, page indicator localization)
- [x] Verified manually on the Android emulator: full English walkthrough, full Spanish walkthrough after switching + restarting, and the first-launch device-locale fallback with a cleared app + Spanish emulator system language
EOF
)"
```
