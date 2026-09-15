# PiccoloReader Roadmap

Tracks progress across all implementation plans. Updated whenever a PR is
opened or merged. Each row's PR link points at the GitHub PR for that
specific task — one small, reviewable PR per task.

## Docs

| Item | Status | PR |
|---|---|---|
| Requirements + architecture design + roadmap | Open for review | [#1](https://github.com/fsoftt/PiccoloReader/pull/1) |

## Plan 1: Project Scaffolding & Library Management

Spec: [`2026-09-14-architecture-design.md`](../specs/2026-09-14-architecture-design.md)
Plan: [`2026-09-14-project-scaffolding-library.md`](2026-09-14-project-scaffolding-library.md)

| # | Task | Status | PR |
|---|---|---|---|
| 1 | Solution scaffolding + CI | Merged | [#3](https://github.com/fsoftt/PiccoloReader/pull/3) |
| 2 | SQLite data layer (Folder, Sheet, AppDatabase) | PR open, awaiting review/merge | [#4](https://github.com/fsoftt/PiccoloReader/pull/4) |
| 3 | LibraryService (folder/sheet CRUD) | Not started | |
| 4 | PdfImportService | Not started | |
| 5 | LibraryViewModel & FolderViewModel | Not started | |
| 6 | DI wiring, navigation skeleton, Library page | Not started | |
| 7 | Root sheets list, move-to-folder, delete prompt polish | Not started | |

## Plan 2: PDF Viewer

Not planned yet — native Android/iOS PDF rasterization, Sheet Viewer
screen, page navigation, zoom/pan.

## Plan 3: Icon Annotations

Not planned yet — icon categories panel, place/move/resize/delete icons,
persistence.

## Plan 4: Pencil Annotations + Eraser

Not planned yet — freehand drawing, color picker, whole-object eraser for
icons and strokes, persistence.

## Plan 5: Undo/Redo

Not planned yet.

## Plan 6: Navigation Polish

Not planned yet — hamburger flyout across folders, final wiring.
