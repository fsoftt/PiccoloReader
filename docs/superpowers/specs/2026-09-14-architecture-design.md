# PiccoloReader — Architecture Design

**Status:** Approved
**Related:** [`docs/requirements.md`](../../requirements.md)

## Overview

PiccoloReader is a local-only .NET MAUI app, targeting **Android and
iOS/iPadOS**, for reading and annotating PDF music sheets. This document
covers the technical architecture: how the app is structured, how PDF pages
are rendered, how the custom annotation system (icons + freehand ink) works,
and how everything is persisted.

No login, no backend, no sync — everything lives in local app storage.
Export/print/flatten-to-PDF is explicitly out of scope for this design.

## Layered Architecture

Four layers, standard MVVM for MAUI:

- **Presentation** — MAUI Pages/Views (Library, Folder, Sheet Viewer, etc.),
  XAML + thin code-behind.
- **ViewModels** — `LibraryViewModel`, `FolderViewModel`,
  `SheetViewerViewModel` (owns current page, active tool, in-memory
  undo/redo stack).
- **Services (platform-agnostic)** — `LibraryService` (folder/sheet CRUD),
  `PdfImportService` (copies a picked PDF into app storage),
  `AnnotationService` (CRUD + persistence for icons/strokes),
  `UndoRedoService`. Pure C#, no UI or platform dependency — fully
  unit-testable.
- **Platform layer** — a single `IPdfPageRenderer` interface (render page N
  of a PDF to a bitmap at a given size), implemented once per platform:
  Android via `android.graphics.pdf.PdfRenderer`, iOS via `PDFKit`.
  Registered through MAUI's dependency injection. This is the **only**
  platform-specific code in the app.
- **Data** — SQLite (`sqlite-net-pcl`) for folder/sheet/annotation metadata;
  app-private file storage for the copied PDF files.

The annotation layer (icons + pencil strokes) is drawn with SkiaSharp on a
canvas overlaid on top of the rasterized PDF page bitmap. It is entirely
our own code, independent of PDF rendering — which is why the
platform-specific surface stays this small despite two OS targets.

### Why native rendering over a third-party PDF library

Considered three options: (A) native per-platform rasterization behind a
shared interface, (B) a third-party cross-platform MAUI PDF library used
only for rasterization, (C) a third-party library's built-in annotation
API. Rejected C because generic PDF annotation APIs (highlight/freetext/ink)
don't support the custom UX here (resizable icon stamps, whole-stroke
eraser) — we'd be building our own layer regardless. Chose A over B: with
only two platforms, both native rasterization APIs are mature, free, and
well-documented; the "savings" from a third-party library mostly don't
materialize once the annotation layer is custom either way, and A avoids a
licensing dependency.

## Data Model

**SQLite tables:**

- **Folder** — `Id`, `Name`
- **Sheet** — `Id`, `FolderId` (nullable — `null` means root), `Title`,
  `FileName` (name of the stored PDF copy), `PageCount`, `DateAdded`
- **Annotation** — `Id`, `SheetId`, `PageNumber`, `Type` (`Icon` |
  `Stroke`), `CreatedAt`
  - Icon-specific: `IconKey` (string identifier, e.g. `"piano"`,
    `"crescendo"` — a string rather than a hard-coded enum, so the icon
    set/categories can grow later without a schema migration), `X`, `Y`,
    `Width`, `Height`
  - Stroke-specific: `ColorHex`, `StrokeWidth`, `Points` (serialized
    polyline — JSON array of normalized x/y pairs)

All position/size values are stored **normalized (0.0–1.0, relative to
page width/height)**, not screen pixels, so annotations stay correctly
placed regardless of device screen size, DPI, or zoom level.

**Folder deletion:** deleting a folder while keeping its sheets sets their
`FolderId` to `null`, moving them to root — falls naturally out of the
nullable-FK model.

**File storage:** each imported PDF is copied into the app's private data
directory (e.g. `Library/Sheets/<sheetId>.pdf`) — fully sandboxed, nothing
depends on the original file the user picked.

## PDF Rendering & Annotation Overlay

Per page, the Sheet Viewer stacks two layers of the same size:

1. A bitmap image — the current page rasterized once via
   `IPdfPageRenderer`, at a resolution scaled for the device (~2x screen
   points, for reasonable pinch-zoom clarity without constant re-rendering).
2. A SkiaSharp canvas on top, transparent except where annotations are
   drawn — reads the page's `Annotation` rows, converts normalized
   coordinates to screen coordinates using the current size, and draws
   icons/strokes.

**Zoom and pan** are a single transform (scale + translate) applied to
*both* layers together, so annotations stay glued to the right spot as the
page moves/scales. No re-rasterization on every zoom tick; if a user zooms
in far past the rendered resolution, the bitmap softens slightly —
acceptable for v1, improvable later (re-render at higher res after zoom
settles) without touching the architecture.

**Page navigation** is one page at a time, swipe/tap to turn — swaps which
page is rasterized and which page's annotations are queried.

## Annotation Editing & Undo/Redo

**Tool panel**, on the Sheet Viewer's right side, opened either by sliding
from the right edge or by tapping a pen-icon button in the toolbar:

- The panel's top row is three icons laid out horizontally — **Music
  icons**, **Pencil**, **Eraser** — acting as a tab selector. Exactly one
  is active at a time; tapping one reveals that tool's section below the
  row, replacing whatever section was showing:
  - **Music icons** section — one accordion per category (categories and
    the icon set itself are TBD, tracked in `requirements.md`); expanding
    a category shows its icons, tap one to place it on the page with a
    default size.
  - **Pencil** section — a size selector and a color selector; finger
    drag on the page draws a new `StrokeAnnotation` in the chosen
    size/color, committed on lift.
  - **Eraser** section — a size selector only. Size sets the **touch
    hit-test radius** used when dragging over the page — a larger radius
    makes small objects easier to catch — but it does not change *what*
    happens on a hit: any annotation (icon or stroke) touched during the
    drag is still deleted **whole**, never partially. Eraser size is a
    transient tool setting (like the active tool itself), not persisted
    per annotation — no data model impact.

The panel stays open across placements/strokes/erases rather than closing
after each action, and can be collapsed by sliding it back or tapping the
toolbar pen icon again.

**Default state (no tool active)** is select/move: tapping an existing
icon on the page selects it directly and shows move/resize handles (drag
body to move, corner handle to resize) — no separate "Select" tool needed.

**Undo/redo**: every committed edit (add icon, move icon, resize icon, add
stroke, delete via eraser) is recorded as a command object with do/undo,
kept on a stack in `SheetViewerViewModel`. The stack is **in-memory only
and resets each time a sheet is opened** — undo/redo covers edits made in
the current viewing session, not persisted across app restarts. Each
committed edit is still persisted to SQLite immediately as it happens, so
closing the app never loses work — it only clears what you *could* undo.

## Navigation & Screens

- **Left hamburger menu** (MAUI Shell flyout) — the app's top-level menu,
  not a folder list. Today it has a single item, **Library**; the menu is
  designed to grow (future items TBD), so it's a real Shell flyout with
  `FlyoutItem`s rather than anything folder-specific.
- **Library page** — lists created folders plus root-level sheets.
  Toolbar (appbar) icons: **Import** (file picker, copies the PDF in via
  `PdfImportService`), **Create folder**, **Sort** (opens a menu to pick
  field — name or date added — and direction; re-sorts the already-loaded
  `ObservableCollection`s client-side in the ViewModel, no repository
  change needed at this data scale). Tap an item to open it (folder ->
  Folder page, sheet -> Sheet Viewer). Long-press opens an action menu:
  Delete on a folder (three-way Delete Sheets / Keep Sheets / Cancel
  alert); Move + Delete on a sheet (Delete is a plain confirm alert; Move
  presents the folder list, including root, to move into).
- **Folder page** — same pattern as the Library page minus the Create
  folder icon (folders don't nest): Import + Sort in the toolbar, tap to
  open a sheet, long-press for the Move/Delete action menu.
- **Sheet Viewer** — the PDF + annotation overlay screen with the right
  sliding tool panel described above, plus a pen-icon toolbar button that
  also opens/closes it.

Navigation is hierarchical push/pop via MAUI Shell (Library -> Folder ->
Sheet Viewer), with the flyout only for top-level destinations — no deep
linking or tabs needed given the flat (single-level) folder structure.

## Testing Approach

- **Unit tests** for the platform-agnostic services —
  `LibraryService`, `AnnotationService`, `UndoRedoService`, coordinate
  normalization math — pure C#, no UI/platform dependency.
- The two `IPdfPageRenderer` implementations (Android/iOS) are thin
  adapters over OS APIs, verified via manual/on-device testing rather than
  unit tests.
- No UI automation framework in this design; can be added later if needed.

## Open Items (tracked, not blocking this design)

- Exact icon set and category groupings (from `requirements.md`)
- Rendering resolution tuning for extreme pinch-zoom (noted above as a
  post-v1 refinement)
