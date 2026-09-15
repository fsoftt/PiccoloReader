# PiccoloReader Roadmap

Tracks progress across all implementation plans. Updated whenever a PR is
opened or merged. Each row's PR link points at the GitHub PR for that
specific task — one small, reviewable PR per task.

## Docs

| Item | Status | PR |
|---|---|---|
| Requirements + architecture design + roadmap | Merged | [#1](https://github.com/fsoftt/PiccoloReader/pull/1) |
| Navigation/toolbar UX redefinition + this rework plan | PR open | [#9](https://github.com/fsoftt/PiccoloReader/pull/9) |

## Plan 1: Project Scaffolding & Library Management

Spec: [`2026-09-14-architecture-design.md`](../specs/2026-09-14-architecture-design.md)
Plan: [`2026-09-14-project-scaffolding-library.md`](2026-09-14-project-scaffolding-library.md)

| # | Task | Status | PR |
|---|---|---|---|
| 1 | Solution scaffolding + CI | Merged | [#3](https://github.com/fsoftt/PiccoloReader/pull/3) |
| 2 | SQLite data layer (Folder, Sheet, AppDatabase) | Merged | [#4](https://github.com/fsoftt/PiccoloReader/pull/4) |
| 3 | LibraryService (folder/sheet CRUD) | Merged | [#5](https://github.com/fsoftt/PiccoloReader/pull/5) |
| 4 | PdfImportService | Merged | [#6](https://github.com/fsoftt/PiccoloReader/pull/6) |
| 5 | LibraryViewModel & FolderViewModel | Merged | [#7](https://github.com/fsoftt/PiccoloReader/pull/7) |
| 6 | DI wiring, navigation skeleton, Library page | Merged | [#8](https://github.com/fsoftt/PiccoloReader/pull/8) |
| 7 | Root sheets list, move-to-folder, delete prompt polish | Superseded — see [Navigation & Toolbar Rework](#navigation--toolbar-rework) below | |

## Navigation & Toolbar Rework

The UX was redefined after Task 6 landed (hamburger flyout, toolbar icons,
long-press action menus, sort) — see
[`2026-09-15-navigation-toolbar-rework.md`](2026-09-15-navigation-toolbar-rework.md),
which supersedes Plan 1's Task 7.

| # | Task | Status | PR |
|---|---|---|---|
| 1 | Add CommunityToolkit.Maui + real Shell flyout | Not started | |
| 2 | Sort support + LibraryViewModel.MoveSheetCommand | Not started | |
| 3 | Rework Library page | Not started | |
| 4 | Rework Folder page | Not started | |

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
