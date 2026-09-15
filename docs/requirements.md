# PiccoloReader — Requirements

## Overview
PiccoloReader is a .NET MAUI app for musicians to read and annotate their music
sheets (PDFs). It is **local-only** — no login, no backend, fully usable
immediately after install.

## Platform
- .NET MAUI, targeting **Android and iOS/iPadOS only**
- Music sheets are **PDFs**
- PDF pages are rasterized via native platform APIs (Android `PdfRenderer`,
  iOS `PDFKit`) — see the architecture spec for why this was chosen over a
  third-party rendering library
- Multi-page sheets are read **one page at a time, swipe/tap to turn**

## Navigation
- A left hamburger menu (drawer) is the app's top-level navigation. Today it
  has one item, **Library**; more items will be added later (the menu is
  designed to grow, not to be Library-specific)
- The Library page and a Folder page (viewing one folder's contents) share
  the same interaction pattern — see below

## Library / Folder Structure
- Flat structure: the root can contain **one level of folders** (no nested
  subfolders) and/or music sheets directly (no folder required)
- Example folders: Orchestra, Big Band, Tropical Group

### Library page
- Shows the created folders, plus any music sheets at root
- Toolbar (appbar) icons:
  - **Import** — add a new music sheet (file picker, PDF only)
  - **Create folder** — add a new folder
  - **Sort** — opens a menu to choose sort field (name / date added) and
    direction (ascending / descending), applied to the list
- **Tap** an item once to open it (a folder navigates to the Folder page; a
  sheet opens the Sheet Viewer)
- **Long-press** an item opens an action menu:
  - On a **sheet**: Move (to another folder or to root), Delete
  - On a **folder**: Delete
- Deleting always shows a confirmation alert first. Deleting a **folder**
  asks a three-way choice: **Delete Sheets** (folder + its sheets are
  removed) / **Keep Sheets** (sheets move to root) / **Cancel**. Deleting a
  **sheet** is a plain confirm/cancel.

### Folder page
- Same pattern as the Library page, minus the **Create folder** icon
  (folders don't nest): Import + Sort in the toolbar, tap to open a sheet,
  long-press for the Move/Delete action menu with the same confirmation
  rules.

## File Storage
- When a music sheet is added to the library, a **copy** of the original PDF
  is stored in a special app-managed location (local app storage, since the
  app is local-only)

## Sheet Viewer & Annotations
Opening a sheet shows the PDF with an annotation layer on top (icons +
freehand pencil strokes).

### Right-side tool panel
- Opened either by sliding from the right edge, or by tapping a pen-icon
  button in the toolbar (appbar)
- The panel's top row is three icons, laid out horizontally: **Music
  icons**, **Pencil**, **Eraser**. Tapping one reveals that tool's section
  below the row:
  - **Music icons** → a list of accordions, one per category (categories
    and the icon set itself are still **TBD**); each accordion expands to
    show that category's icons — tap one to place it on the page
  - **Pencil** → a size selector and a color selector; drag on the page to
    draw a stroke in the chosen size/color
  - **Eraser** → a size selector. Size controls the **touch hit-test
    radius** only — a bigger eraser makes it easier to catch small objects,
    but whatever is touched is still deleted **whole** (icon or stroke),
    not partially erased

### Music icons (stamps)
- Icons can be moved, resized, and removed individually (tap to select,
  drag handles to move/resize; eraser deletes the whole icon)

### Freehand drawing (pencil)
- Drawn with finger (touch input), configurable size and color
- Eraser deletes a touched stroke **entirely** — whole-object delete, same
  as icons (see eraser size note above)

### Persistence
- Annotations (icons + pencil strokes) are saved per music sheet and reloaded
  automatically when the sheet is reopened

### Undo / Redo
- Undo/redo is in scope for the annotation workflow, scoped to the current
  viewing session (resets when the sheet is closed and reopened)

## Open Questions / Not Yet Decided
- Exact music icon set and category groupings (dynamics, articulations, etc.)
