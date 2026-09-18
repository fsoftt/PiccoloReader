# Multi-Language Support (English + Spanish) — Design

**Status:** Approved

## Overview

PiccoloReader currently has no localization infrastructure — every
user-facing string is a hardcoded literal in XAML or code-behind, in
both the App project (`Views/`) and `PiccoloReader.Core` (the Music
Icons category/label names in `MusicIconCatalog`). This plan adds
English + Spanish support, a "Settings" flyout menu item, and a Settings
page where the language can be changed. Language changes take effect on
next app restart, not live — confirmed acceptable, since it avoids the
much larger complexity of a reactive/live-rebinding localization
mechanism for a two-language, restart-tolerant app.

## Resource Files: Location and Mechanism

Resource files live in **`PiccoloReader.Core`**, not the App project,
because `MusicIconCatalog`'s category/icon names are Core-only and Core
cannot depend on the App project (dependency direction is App → Core).
Two files:

- `src/PiccoloReader.Core/Resources/Strings/AppStrings.resx` — English,
  the neutral/default culture and fallback.
- `src/PiccoloReader.Core/Resources/Strings/AppStrings.es.resx` —
  Spanish. Same keys, translated values.

Both are plain `.resx`, embedded automatically by the SDK-style csproj
(no extra `<EmbeddedResource>` wiring needed) — `.resx` files are
embedded resources by default.

**No IDE-generated `Designer.cs`.** `PublicResXFileCodeGenerator` /
`ResXFileCodeGenerator` (the classic resx-to-strongly-typed-class
tooling) is a Visual Studio custom-tool mechanism that does not reliably
fire on a plain `dotnet build` from the CLI — this project's entire
workflow so far has been CLI-only (no Visual Studio in the loop), so
relying on it is a real risk, not a theoretical one. Instead, one
hand-written static class:

```csharp
// src/PiccoloReader.Core/Resources/Strings/AppStrings.cs
namespace PiccoloReader.Core.Resources.Strings;

public static class AppStrings
{
    private static readonly ResourceManager ResourceManager =
        new("PiccoloReader.Core.Resources.Strings.AppStrings", typeof(AppStrings).Assembly);

    public static string Library => Get(nameof(Library));
    public static string Folder => Get(nameof(Folder));
    // ...one property per key, all following this exact shape...

    private static string Get(string key) =>
        ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
```

Falling back to the key itself (not throwing, not returning empty) if a
translation is ever missing keeps a missed string visible-but-obviously-
wrong during development rather than silently blank. Every property is a
one-line mechanical addition — adding a new string is: add the key to
both `.resx` files, add one property to `AppStrings.cs`.

**Usage:**
- XAML: `{x:Static strings:AppStrings.Library}` — evaluated once when
  the page is constructed, which is exactly when it needs to be
  reevaluated (page reconstruction after restart), given the
  restart-required decision.
- Code-behind (`DisplayAlert`/`DisplayActionSheet`/dynamic labels):
  `AppStrings.SomeKey` directly, `string.Format` for parameterized
  strings (e.g. the page indicator "Page {0} of {1}").

## String Inventory (scope, not exhaustive here)

Covers all hardcoded strings found across:
- `AppShell.xaml` (flyout: "Library" item, new "Settings" item, footer
  credit — "Made with love by" stays, `@fsoftt` handle is not a
  translatable string) — page/section titles.
- `LibraryPage`, `FolderPage`, `SheetViewerPage` — titles, placeholders,
  dialog/action-sheet titles and options ("Cancel", "Delete", "Move",
  "Create Folder", "Sort by", the four sort option labels, bookmark
  dialog strings, etc.), the page indicator format string.
- `MusicIconCatalog` (Core) — category names ("Dynamics",
  "Articulations", "Fermata & Breath", "Hairpins") and icon labels
  ("Staccato", "Accent", "Tenuto", "Marcato", "Fermata", "Breath mark",
  "Crescendo", "Decrescendo"). The short dynamics markings themselves
  (`pp`, `p`, `f`, `ff`, `fp`) are standard musical notation, not
  language-specific words — left untranslated (same value in both
  `.resx` files).
- New: the "Settings" flyout item's own label, and every string on the
  new Settings page (see below).

**Not translated** (brand/identity, not UI copy): the "Piccolo" wordmark,
the app's own name "PiccoloReader", the `@fsoftt` credit handle and its
URL.

The exact key list is an implementation-plan-level detail (mechanical:
one key per string found), not enumerated exhaustively in this design
doc.

## Language Preference: Storage and Resolution

**Storage** — MAUI's built-in `Preferences` API (key `"AppLanguage"`,
value `"en"` or `"es"`), the same App-project-only Storage.Essentials
surface used elsewhere. Following the existing `IAppStorageProvider` /
`MauiAppStorageProvider` split already in this codebase (Core defines
the interface, App project implements it against MAUI APIs, wired via
DI in `MauiProgram`), a small new interface:

```csharp
// PiccoloReader.Core.Services
public interface ILanguagePreferenceService
{
    string? GetSavedLanguageCode();      // null if never set
    void SaveLanguageCode(string code);  // "en" or "es"
}
```

implemented in the App project as `MauiLanguagePreferenceService` using
`Preferences.Default`, registered `AddSingleton` alongside
`IAppStorageProvider`. This keeps `SettingsViewModel` (Core) constructor-
injectable and unit-testable without a MAUI Essentials dependency in
Core, matching every other ViewModel in this codebase.

**Startup resolution** (`MauiProgram.CreateMauiApp()`, before
`builder.Build()` — must run before any page/resource is touched):

1. Read the saved preference directly via `Preferences.Default.Get("AppLanguage", (string?)null)`.
2. Read the device's current UI culture's two-letter code
   (`CultureInfo.CurrentUICulture.TwoLetterISOLanguageName`).
3. Feed both into a pure resolver function in Core:

```csharp
// PiccoloReader.Core.Services.LanguageResolver (static, pure — unit-testable)
public static string ResolveLanguageCode(string? savedCode, string deviceCode) =>
    savedCode ?? (deviceCode == "es" ? "es" : "en");
```

4. If step 1 returned null (first launch), persist the resolved code
   immediately via `Preferences.Default.Set(...)` so it's stable from
   here on (no re-detecting device locale on every launch — once
   resolved, the user's implicit "first launch" choice sticks until they
   explicitly change it in Settings).
5. Set `CultureInfo.CurrentCulture`, `CultureInfo.CurrentUICulture`,
   `CultureInfo.DefaultThreadCurrentCulture`, and
   `CultureInfo.DefaultThreadCurrentUICulture` to the resolved culture
   (`new CultureInfo(code)`) — the `DefaultThreadCurrent*` pair matters
   because MAUI page construction and resource lookups can happen off
   the thread that ran `CreateMauiApp()`.

This is bootstrapping code, not part of the testable ViewModel layer —
it lives directly in `MauiProgram.cs`, matching how this file already
does other one-time startup work (e.g. the synchronous
`AppDatabase.InitializeAsync()` call).

## Settings Page

**Flyout entry** — a new `FlyoutItem` in `AppShell.xaml`, positioned
after "Library", with a `FlyoutIcon` reusing the same tune/gear glyph
(`&#xE429;`, `MaterialOutlined`) already proven working as the sheet
viewer's tool-config icon — same icon family, same "settings/config"
meaning, no new glyph to risk getting wrong. Label: `AppStrings.Settings`
("Settings" / "Ajustes").

**Page content** (`SettingsPage.xaml` + `SettingsViewModel` in Core,
`SettingsPage` registered as a transient view + DI-registered ViewModel,
matching `LibraryPage`/`FolderPage`/`SheetViewerPage`'s existing
registration pattern in `MauiProgram.cs`):

- A "Language" section header, then two selectable rows — "English" and
  "Español" — styled with the app's existing `ListItemCard` border style
  for visual consistency with every other list row in the app (Folders,
  Sheets, Music Icons). The row matching the current language shows a
  checkmark (reusing the app's established selection-indicator visual
  language — the same kind of highlight already used for the selected
  pencil color swatch on the sheet viewer).
- Tapping a row: if it's not already selected,
  `SettingsViewModel.SetLanguageCommand` saves the new code via
  `ILanguagePreferenceService`, updates `SelectedLanguageCode`
  (refreshing the checkmark), and the page shows a `DisplayAlert` —
  "Restart PiccoloReader for the new language to take effect." /
  "Reinicia PiccoloReader para que el nuevo idioma tenga efecto." (in
  *whatever the current, not-yet-changed, session's language still is*
  — the culture doesn't change until restart, so this dialog is correct
  either way). No programmatic app-restart — MAUI has no reliable
  cross-platform "restart the app" API, and hand-rolling a
  platform-specific one (Android-only, via relaunching the main
  activity) is out of scope for what this feature needs.
- The page is a plain, single-purpose layout — not a dynamic/extensible
  "settings list" framework. Future settings (if any are ever added) get
  added to this same page directly when that need is concrete; building
  speculative infrastructure for hypothetical future options now would
  be scope creep with no present requirement to justify it.

## Testing

Pure-C# pieces get xUnit coverage in `PiccoloReader.Core.Tests`, same
split as every prior plan:

- `LanguageResolver.ResolveLanguageCode` — saved code wins when present;
  falls back to device code only when `"es"`; falls back to `"en"` for
  every other device code (including null/empty/unrecognized).
- `SettingsViewModel` — `SelectedLanguageCode` initializes from
  `ILanguagePreferenceService.GetSavedLanguageCode()` (via a fake
  implementation, mirroring how existing ViewModel tests fake their
  service dependencies); `SetLanguageCommand` calls
  `SaveLanguageCode` with the right value and updates
  `SelectedLanguageCode`.
- `AppStrings` — spot-check that a couple of keys resolve to different,
  non-null values under `en` vs `es` `CultureInfo.CurrentUICulture`
  (catches a missing `.resx` entry or a `ResourceManager` base-name
  typo without needing to check every single key by hand).

Everything else (flyout icon rendering, the Settings page's row
selection UI, the restart dialog, and confirming every existing page
actually shows Spanish text when the culture is set to `es`) is
Presentation-layer — verified on-device per task, matching how every
other UI change in this app has been verified so far (no automated UI
test framework in this project). The full-app Spanish pass in
particular needs a manual walkthrough of every page with the culture
forced to `es`, since a hand-written `AppStrings` class has no compiler
check that every key that exists in English also has a Spanish entry —
a missing key falls back to the key name itself (by design, per the
`Get` helper above), which manual verification would catch as visibly
wrong untranslated text.

## Out of Scope

- **Live/reactive language switching** — explicitly deferred; restart
  required, confirmed acceptable.
- **More than two languages** — the mechanism (per-culture `.resx` +
  resolver fallback) extends to a third language by adding one more
  `.resx` file and one more Settings row, but nothing beyond English +
  Spanish is being built now.
- **A general/extensible settings framework** — this page is
  purpose-built for language only; see "Settings Page" above.
- **Programmatic app restart** — the dialog asks the user to restart
  manually.
- **Translating brand identity** — "Piccolo", "PiccoloReader", and the
  `@fsoftt` credit stay as-is in both languages.
