# Pencil Annotations + Eraser — Design

**Status:** Approved
**Related:** [`2026-09-14-architecture-design.md`](2026-09-14-architecture-design.md) (base architecture — Data Model and Annotation Editing & Undo/Redo sections), [`../plans/roadmap.md`](../plans/roadmap.md) (Plan 4)

## Overview

Plan 3 (Icon Annotations) shipped icon stamps only, with a single-purpose
tool panel. This plan adds the other two tools the architecture spec's
"Annotation Editing" section always described alongside icons — **Pencil**
(freehand ink strokes, chosen color/width) and **Eraser** (whole-object
delete by dragging over icons or strokes) — plus the three-tab panel
structure needed to host all three tools. Undo/redo is explicitly deferred
to Plan 5, matching how Plan 3 shipped without it.

## Data Model

Extend the existing `Annotation` table (`src/PiccoloReader.Core/Data/Models/Annotation.cs`)
rather than introducing a second table — `CreateTableAsync<T>()` auto-adds
new columns to an existing SQLite table, the same mechanism already used
for `Sheet.LastViewedPageIndex`, so this needs no migration code. Existing
icon rows are unaffected; their new columns stay unused.

New columns:

- `Type` (`string`) — `"Icon"` or `"Stroke"`. Existing rows predate this
  column and read back as `null`/empty from SQLite; `AnnotationService`
  treats a missing/empty `Type` as `"Icon"` for backward compatibility, so
  no backfill is required.
- `ColorHex` (`string`) — stroke color, e.g. `"#000000"`. Unused for icons.
- `StrokeWidth` (`double`) — **normalized**, as a fraction of page width,
  matching how icon `Width`/`Height` are normalized (0.0–1.0). Storing it
  normalized (not in fixed points) keeps a stroke's rendered thickness
  visually consistent relative to the page regardless of device screen
  size or zoom level — the same reasoning the architecture spec already
  gives for normalizing position/size. Unused for icons.
- `Points` (`string`) — JSON array of normalized `{x, y}` pairs (e.g.
  `[{"X":0.12,"Y":0.34},...]`), serialized with `System.Text.Json`. A
  `StrokePoint(double X, double Y)` record is the serialization shape.
  Unused for icons.

`IconKey`/`X`/`Y`/`Width`/`Height` (existing columns) are unused for
stroke rows and are deliberately **not** repurposed to hold a stroke's
bounding box — nothing in this plan needs one, since strokes are neither
selectable nor movable (see Out of Scope). If a stroke's extent is ever
needed later, it can always be derived from `Points`.

`AnnotationService` gains:

```csharp
Task<Annotation> AddStrokeAsync(int sheetId, int pageIndex, string colorHex, double strokeWidth, IReadOnlyList<StrokePoint> points);
```

alongside the existing `AddIconAsync`. `DeleteAnnotationAsync` (existing)
already works for either type — it deletes by row, with no type-specific
logic. `GetAnnotationsAsync` (existing) already returns all rows for a
page regardless of type — no change needed; `SheetViewerViewModel`'s
`CurrentPageAnnotations` stays a single unified `ObservableCollection<Annotation>`
holding both icons and strokes, exactly as it does today for icons alone.

## Tool Panel: Three Tabs

`ToolPanel` (`SheetViewerPage.xaml`) currently shows the Music Icons
picker unconditionally. This plan restructures it with a tab row at the
top — three tappable icon buttons (Music Icons / Pencil / Eraser) — with
the active tab visually highlighted and its section shown below, matching
the architecture spec's "acting as a tab selector... exactly one active
at a time."

**Tab selection never opens or closes the panel.** The panel's open/closed
state is controlled only by the existing mechanisms (toolbar pen-icon
toggle, tap-outside-to-close) — completely independent of which tab is
selected. This matters because Pencil and Eraser both need the user to
pick a color/width or size *before* drawing/erasing, which requires the
panel to stay open across that selection.

Whichever tab is selected **is** the active tool, whether the panel is
open or closed. Music Icons active means normal select/move page behavior
(unchanged from today: tap selects an icon, drag pans/zooms or turns
pages). Pencil or Eraser active means dragging on the page draws or
erases instead (see below) — this applies whether or not the panel
happens to be open at the time, though in practice the user will normally
close the panel before dragging on the page since the panel visually
covers part of it.

**Toolbar changes:** the existing pen icon keeps its current job (toggle
panel open/closed) unchanged. A second icon — an **X** — appears in the
toolbar *only* while Pencil or Eraser is the active tab. Tapping it
switches the active tab back to Music Icons (i.e. deactivates the drawing
tool, back to plain select/move) without forcing the panel open or
closed. Once deactivated, the X disappears, leaving just the pen icon,
same as today.

**Panel content per tab:**

- **Music Icons** — unchanged: the existing category grid.
- **Pencil** — a row of 5 color swatches (Black, Red, Blue, Green,
  Orange — a fixed, curated palette, consistent with the Music Icons
  picker's own curated-not-searchable style) plus a stroke-width slider.
  Default on first activation this session: Black, a middle-of-range
  width. The app remembers the last-used color/width for the rest of the
  viewing session (in-memory `SheetViewerViewModel` state, not persisted
  to SQLite) so switching tabs and back doesn't reset the choice.
- **Eraser** — a single hit-test-radius slider, same default-remembered
  behavior. Slider range and defaults are a Presentation-layer detail the
  implementation plan can pick concretely (e.g. 20–80 device-independent
  pixels at the default zoom, defaulting to the middle) — not
  architecturally significant.

## Pencil: Drawing Mechanics

Freehand drawing must work at any zoom level and coexist with
`PageContainer`'s existing `PinchGestureRecognizer`/`PanGestureRecognizer`/`TapGestureRecognizer`.
Rather than adding a new gesture recognizer — which, per two real bugs
already hit and fixed in this app (Pan+Swipe on one element in Plan 2;
Tap on a covering child vs. Pinch+Pan on its parent in Plan 3's PR #36 —
see `SheetViewerPage.xaml.cs`'s existing comments), reliably causes
gesture-arena conflicts — this plan **extends the existing `OnPanUpdated`
handler** with a third behavior, branching on which tool tab is active:

- **Music Icons active** (today's only behavior): pan either drags the
  zoomed page around, or (not zoomed) turns pages on a large enough
  swipe. Unchanged.
- **Pencil active**: a drag draws. On `GestureStatus.Started`, begin a new
  in-progress point list. On `Running`, convert the current touch
  position to a normalized page coordinate — reusing the zoom-aware
  conversion formula already fixed for move/resize in PR #39
  (`screenDelta / (PageContainer.Width * _currentScale)`, and the
  equivalent absolute-position form for the touch point itself) — append
  it to the in-progress list, and repaint immediately so ink appears
  live under the finger. On `Completed`, if the stroke has at least 2
  points, commit it via `AnnotationService.AddStrokeAsync` using the
  currently-selected color/width, add the resulting `Annotation` to
  `CurrentPageAnnotations`, and clear the in-progress buffer. A stroke
  that never leaves its starting point (a tap, not a drag) is discarded,
  not committed as a zero-length stroke.
- **Eraser active**: see below.

Stroke rendering reuses the existing `AnnotationCanvas` SkiaSharp surface
(`OnAnnotationCanvasPaintSurface`) that already draws icons — no new
canvas. Each stroke annotation draws as an `SKPath` (built from its
deserialized `Points`, converted from normalized to the canvas's pixel
space the same way icon positions already are) stroked with `ColorHex`/
`StrokeWidth` via `SKPaint` (`Style = SKPaintStyle.Stroke`, round caps/
joins for a natural pencil feel). The in-progress (uncommitted) stroke
currently being drawn is painted the same way, read from a field on the
page rather than from `CurrentPageAnnotations` (it isn't committed yet).

## Eraser: Mechanics

Also implemented inside `OnPanUpdated`'s Eraser branch: a drag is
hit-tested continuously along its path rather than drawing.

- At each point along the drag (both the touch-down position and every
  `Running` update), test every annotation on the current page:
  - **Icon** — the existing bounding-box test already used for
    tap-to-select in `OnPageContainerTapped`.
  - **Stroke** — the distance from the touch point to the nearest point
    on the stroke's polyline (iterate consecutive point pairs, per-segment
    point-to-segment distance, take the minimum).
  - If either falls within the eraser's current hit-test radius
    (normalized the same zoom-aware way as everything else, converted
    from its DIP slider value via `radius / (PageContainer.Width *
    _currentScale)`), delete that annotation immediately via the existing
    `AnnotationService.DeleteAnnotationAsync` and remove it from
    `CurrentPageAnnotations` — matching how the drag-to-trash delete
    already works. Erasing is whole-object only, matching the
    architecture spec explicitly — never partial-stroke.
- While dragging, a translucent circle is drawn at the current touch
  point, radius matching the hit-test radius, giving live visual feedback
  of what's about to be caught. Not called out explicitly in the
  architecture spec, but necessary — without it there's no way to judge
  whether a drag is about to erase something before it happens.

## Out of Scope

- **Undo/redo** — deferred to Plan 5 (already scoped in the roadmap).
  Every stroke add and every eraser delete commits to SQLite immediately,
  exactly like icon add/move/resize/delete already do.
- **Selecting, moving, or resizing a placed stroke.** Once drawn, a
  stroke can only be removed (via the Eraser) — never moved, resized, or
  reselected the way icons can. The existing tap-to-select hit-test in
  `OnPageContainerTapped` is not extended to strokes; tapping on or near
  a stroke while in Music Icons/select mode has no special effect (falls
  through to tap-to-turn-page, same as tapping any other blank area).
  Nothing in the architecture spec's Pencil/Eraser description calls for
  stroke selection, and adding it would mean designing a hit-test,
  overlay, and resize interaction for an arbitrary polyline shape — a
  meaningfully larger scope than what's needed here.
- **Full/custom color picker.** The 5-color fixed palette is deliberate,
  not a placeholder — matches the existing Music Icons picker's curated,
  tap-to-choose style rather than introducing a different interaction
  pattern for one tool.

## Testing

Pure-C# pieces get xUnit coverage in `PiccoloReader.Core.Tests`, same
split as every prior plan:

- `AnnotationService.AddStrokeAsync` — persists and round-trips a stroke
  (color, width, points) the same way existing `AddIconAsync` tests work.
- `StrokePoint` JSON serialization round-trip (a list of points survives
  serialize → deserialize unchanged).
- The eraser's point-to-polyline distance calculation — pure geometry,
  independently testable without any MAUI/SkiaSharp dependency.
- `Annotation.Type` backward-compatibility — a row with no `Type` set
  (simulating a pre-migration icon row) is still treated as an icon.

Gesture handling, live-drawing rendering, the tab switcher, and the
toolbar X icon are Presentation-layer — verified on-device per task,
matching how every other on-page interaction in this app has been
verified so far (no automated UI test framework in this project).
