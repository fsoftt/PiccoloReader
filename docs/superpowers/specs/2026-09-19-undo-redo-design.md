# Undo/Redo for Sheet Annotations — Design

## Goal

Let the user undo and redo annotation edits (icon placement, icon
move/resize, icon/stroke deletion via the delete button or the eraser,
and pencil strokes) while viewing a sheet, via two toolbar buttons.

## Scope

- Session-scoped, in-memory only. Undo/redo history is not persisted —
  it starts empty when a sheet is opened and is gone once you leave the
  Sheet Viewer or navigate to a different page within the same sheet.
- Covers every annotation-mutating action already exposed by
  `SheetViewerViewModel`: `PlaceIconAsync`, `AddStrokeAsync`,
  `MoveSelectedAnnotationAsync`, `ResizeSelectedAnnotationAsync`,
  `DeleteSelectedAnnotationAsync`, `EraseAnnotationAsync`.
- Does not cover bookmarks, page navigation, or tool/color/width
  selection — only edits to the annotations on the page.

## Architecture

### Reversible action types (new, in `PiccoloReader.Core`)

No MAUI dependency; each type is independently unit-testable.

```csharp
public interface IUndoableAction
{
    Task UndoAsync();
    Task RedoAsync();
}
```

- **`AddAnnotationAction`** — wraps one `Annotation` plus the
  `ObservableCollection<Annotation>` it belongs to (the ViewModel's
  `CurrentPageAnnotations`).
  - `UndoAsync()`: delete the annotation via `AnnotationService`, remove
    it from the collection.
  - `RedoAsync()`: re-insert it via `AnnotationService`, re-add it to
    the collection.
  - Covers `PlaceIconAsync` and `AddStrokeAsync` — both are "insert one
    new annotation," so they share this one action type.

- **`DeleteAnnotationAction`** — the exact inverse of
  `AddAnnotationAction`, same two dependencies.
  - `UndoAsync()`: re-insert + re-add to the collection.
  - `RedoAsync()`: delete + remove from the collection.
  - Covers `DeleteSelectedAnnotationAsync` and each individual hit from
    `EraseAnnotationAsync`.

- **`UpdateAnnotationAction`** — wraps an `Annotation` plus its
  before/after `(X, Y, Width, Height)`.
  - `UndoAsync()`: apply the before-values to the annotation, persist
    via `AnnotationService.UpdateAnnotationAsync`.
  - `RedoAsync()`: apply the after-values, persist the same way.
  - Covers `MoveSelectedAnnotationAsync` and
    `ResizeSelectedAnnotationAsync`.

- **`CompositeUndoAction`** — wraps an ordered
  `IReadOnlyList<IUndoableAction>`.
  - `UndoAsync()`: undo every sub-action, in reverse order.
  - `RedoAsync()`: redo every sub-action, in original order.
  - Used only to group a single eraser drag that removed more than one
    annotation into one undo step (see below).

### `AnnotationService` addition

```csharp
public Task InsertAnnotationAsync(Annotation annotation) =>
    _database.Connection.InsertAsync(annotation);
```

A generic re-insert of an existing, fully-populated `Annotation`
object — needed so `AddAnnotationAction.RedoAsync()` and
`DeleteAnnotationAction.UndoAsync()` can restore a previously-deleted
row without going through the icon/stroke-specific constructors
(`AddIconAsync`/`AddStrokeAsync`, which build a *new* `Annotation` from
scratch). Re-inserting the same object gets a new auto-incremented
`Id` from SQLite; nothing depends on IDs staying stable across an
undo/redo cycle since everything downstream (the ObservableCollection,
`SelectedAnnotation`) holds the same C# object by reference.

### `SheetViewerViewModel` integration

- Two private fields: `Stack<IUndoableAction> _undoStack`,
  `Stack<IUndoableAction> _redoStack`.
- `[RelayCommand(CanExecute = nameof(CanUndo))] private async Task
  UndoAsync()` / the matching `RedoAsync()` — same
  `CanExecute`-predicate pattern already used by `NextPageCommand` and
  `DeleteSelectedAnnotationCommand`. `CanUndo()` / `CanRedo()` just
  check `_undoStack.Count > 0` / `_redoStack.Count > 0`.
- Two `[ObservableProperty] private bool _canUndo;` / `_canRedo`
  properties, kept in sync alongside the commands' own CanExecute (see
  UI section below — this page's toolbar items use `Clicked` handlers
  rather than `Command` bindings, so they need an explicit bindable
  bool rather than relying on command auto-disable).
- Every mutating method pushes the matching action onto `_undoStack`
  right after its existing DB + collection work, clears `_redoStack`,
  updates `CanUndo`/`CanRedo`, and calls
  `UndoCommand.NotifyCanExecuteChanged()` /
  `RedoCommand.NotifyCanExecuteChanged()`.
- `UndoAsync()` pops `_undoStack`, calls `action.UndoAsync()`, pushes
  the action onto `_redoStack`. `RedoAsync()` is the mirror image.
- Both stacks are cleared whenever a new page loads
  (`LoadCurrentPageAsync`), so undo/redo never crosses a page
  boundary. Because `SheetViewerViewModel` is DI-registered
  `Transient`, opening a different sheet also starts with empty stacks
  with no extra reset code needed.

### Signature changes

The live drag handlers in `SheetViewerPage.xaml.cs` already mutate the
selected annotation's `X`/`Y`/`Width`/`Height` directly for real-time
visual feedback while the finger is moving, so by the time
`MoveSelectedAnnotationAsync`/`ResizeSelectedAnnotationAsync` are
called (once, at gesture end), the "before" value already only exists
in the page's own `_moveStartX`/`_moveStartY` /
`_resizeStartWidth`/`_resizeStartHeight` fields, not on the annotation
object. Both methods gain a "before" parameter pair so the ViewModel
can build the `UpdateAnnotationAction`:

- `MoveSelectedAnnotationAsync(newX, newY)` →
  `MoveSelectedAnnotationAsync(oldX, oldY, newX, newY)`
- `ResizeSelectedAnnotationAsync(newWidth, newHeight)` →
  `ResizeSelectedAnnotationAsync(oldWidth, oldHeight, newWidth,
  newHeight)`

If `old == new` (e.g. a cancelled drag that snaps back to its start
position), no action is pushed — an undo step should never be a no-op.

### Selection after undo/redo

The action types themselves only know about `AnnotationService` and
the page's `ObservableCollection<Annotation>` — they don't have (and
shouldn't need) a reference to the ViewModel's `SelectedAnnotation`.
Instead, `SheetViewerViewModel.UndoAsync()`/`RedoAsync()` do one
generic check after calling `action.UndoAsync()`/`RedoAsync()`: if
`SelectedAnnotation` is non-null and no longer present in
`CurrentPageAnnotations`, clear it. This handles every action type
uniformly (including a multi-item `CompositeUndoAction`) with no
per-action-type special-casing, and mirrors what
`DeleteSelectedAnnotationAsync`/`EraseAnnotationAsync` already do today
when deleting the selected annotation directly. Re-adding an
annotation does not automatically select it.

### Eraser batching

A single eraser drag can pass over several annotations, calling
`EraseAnnotationAsync` once per hit. Undoing that should restore
everything from one drag in one step, not one press per erased item.

`SheetViewerViewModel` gains `BeginEraseBatch()` / `EndEraseBatch()`.
The page's existing `OnEraserDrawingLineStarted` /
`OnEraserDrawingLineCompleted` / `OnEraserDrawingLineCancelled`
handlers call these to bracket a drag gesture. While a batch is open,
`EraseAnnotationAsync` appends each `DeleteAnnotationAction` to a
pending list instead of pushing it directly. `EndEraseBatch()` wraps
that list in one `CompositeUndoAction` and pushes it as a single undo
step (or pushes nothing if the drag erased zero annotations).

## UI

Two new `ToolbarItem`s in `SheetViewerPage.xaml`, alongside the
existing back/bookmark icons. This page's existing toolbar items all
use `Clicked="OnXClicked"` code-behind handlers rather than XAML
`Command` bindings (see `ToolConfigItem`/`BookmarksItem`), so the new
items follow that same pattern for consistency: `Clicked="OnUndoClicked"`
/ `Clicked="OnRedoClicked"` call `_viewModel.UndoCommand.ExecuteAsync(null)`
/ `RedoCommand.ExecuteAsync(null)`. Auto-graying-out is done via
`IsEnabled="{Binding CanUndo}"` / `{Binding CanRedo}` bound to two new
`[ObservableProperty] bool` properties on the ViewModel, updated
alongside `NotifyCanExecuteChanged()` every time either stack changes.

## Testing

- `AddAnnotationAction`, `DeleteAnnotationAction`,
  `UpdateAnnotationAction`, and `CompositeUndoAction` each get direct
  unit tests in `PiccoloReader.Core.Tests` — construct against a real
  in-memory `AnnotationService`/`AppDatabase` plus a plain
  `ObservableCollection<Annotation>`, call `UndoAsync()`/`RedoAsync()`,
  assert both the DB rows and the collection contents.
- `SheetViewerViewModelTests` gets new tests covering: undo/redo of
  each of the six mutating methods; the redo stack being cleared by a
  new action; both stacks resetting on page change; the eraser-batch
  behavior producing exactly one undo step for a multi-hit drag; and
  no-op moves/resizes not pushing an action.
