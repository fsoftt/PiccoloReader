# PiccoloReader — Requirements

## Overview
PiccoloReader is a .NET MAUI app for musicians to read and annotate their music
sheets (PDFs). It is **local-only** — no login, no backend, fully usable
immediately after install.

## Platform
- .NET MAUI (cross-platform: iOS, Android, Windows, macOS — target platforms TBD)
- Music sheets are **PDFs**
- A third-party library will be used for PDF rendering (MAUI has no built-in
  PDF support) — specific library TBD

## Library / Folder Structure
- Flat structure: the root can contain **one level of folders** (no nested
  subfolders) and/or music sheets directly (no folder required)
- Example folders: Orchestra, Big Band, Tropical Group
- Deleting a folder prompts the user: delete the music sheets inside, or keep
  them (presumably moved to root — TBD, see open questions)
- A music sheet can be:
  - Moved between folders (or to/from root)
  - Deleted

## File Storage
- When a music sheet is added to the library, a **copy** of the original PDF
  is stored in a special app-managed location (local app storage, since the
  app is local-only)

## Annotations
Two annotation types, layered on top of the PDF page:

### 1. Music icons (stamps)
- A user can add standard music icons (piano, pianissimo, forte-piano,
  crescendo, etc.) — **exact icon set to be defined later**
- Icons can be:
  - Moved
  - Resized
  - Removed (individually)
- Eraser tool deletes icons **entirely** (whole-icon delete, not partial)

### 2. Freehand drawing (pencil)
- User draws with finger (touch input)
- User can select pencil color
- Eraser tool deletes pencil strokes **entirely** — as soon as the eraser
  touches a stroke, the whole stroke is removed (same whole-object delete
  behavior as icons)

### Persistence
- Annotations (icons + pencil strokes) are saved per music sheet and reloaded
  automatically when the sheet is reopened

### Undo / Redo
- Undo/redo is in scope for the annotation workflow

## Open Questions / Not Yet Decided
- Exact music icon set (dynamics, articulations, etc.)
- When a folder is deleted and sheets are kept, where do they go? (implied:
  root)
- PDF rendering library choice
- Target platforms (iOS / Android / Windows / macOS — all or subset)
- Multi-page PDF handling (annotations presumably per-page)
