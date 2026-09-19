# PiccoloReader Roadmap

Tracks progress across all implementation plans. Updated whenever a PR is
opened or merged. Each row's PR link points at the GitHub PR for that
specific task — one small, reviewable PR per task.

## Docs

| Item | Status | PR |
|---|---|---|
| Requirements + architecture design + roadmap | Merged | [#1](https://github.com/fsoftt/PiccoloReader/pull/1) |
| Navigation/toolbar UX redefinition + this rework plan | Merged | [#9](https://github.com/fsoftt/PiccoloReader/pull/9) |

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
| 1 | Add CommunityToolkit.Maui + real Shell flyout | Merged | [#11](https://github.com/fsoftt/PiccoloReader/pull/11) |
| 2 | Sort support + LibraryViewModel.MoveSheetCommand | Merged | [#12](https://github.com/fsoftt/PiccoloReader/pull/12) |
| 3 | Rework Library page | Merged | [#13](https://github.com/fsoftt/PiccoloReader/pull/13) |
| 4 | Rework Folder page | Merged | [#14](https://github.com/fsoftt/PiccoloReader/pull/14) |

## UI Polish

Bounded UI tasks done outside the numbered plans above.

| Item | Status | PR |
|---|---|---|
| Dark theme toolbar contrast, list item cards, folder/file icons | Merged | [#15](https://github.com/fsoftt/PiccoloReader/pull/15) |
| Animated Lottie splash screen | Merged | [#16](https://github.com/fsoftt/PiccoloReader/pull/16) |
| CI: build + upload Release APK on push to main | Merged | [#17](https://github.com/fsoftt/PiccoloReader/pull/17) |

## Plan 2: PDF Viewer

Spec: [`2026-09-14-architecture-design.md`](../specs/2026-09-14-architecture-design.md)
Plan: [`2026-09-15-pdf-viewer.md`](2026-09-15-pdf-viewer.md)

Scoped to Android only — no Mac/iOS simulator available to build or verify
an iOS `IPdfPageRenderer` against; iOS gets a `NotImplementedException`
stub until that changes.

| # | Task | Status | PR |
|---|---|---|---|
| 1 | IPdfPageRenderer interface + Android implementation | Merged | [#18](https://github.com/fsoftt/PiccoloReader/pull/18) |
| 2 | LibraryService.GetSheetAsync / UpdateSheetPageCountAsync | Merged | [#19](https://github.com/fsoftt/PiccoloReader/pull/19) |
| 3 | SheetViewerViewModel | Merged | [#20](https://github.com/fsoftt/PiccoloReader/pull/20) |
| 4 | SheetViewerPage (zoom/pan, tap/swipe page turning) | Merged | [#21](https://github.com/fsoftt/PiccoloReader/pull/21) |
| 5 | Routing + tap-to-open wiring + end-to-end verification | Merged | [#22](https://github.com/fsoftt/PiccoloReader/pull/22) |

**Post-merge fixes/polish** (found via testing PR #22):

| Item | Status | PR |
|---|---|---|
| Gesture fixes: pinch-zoom shake, swipe/pan gesture-arena conflict | Merged | [#23](https://github.com/fsoftt/PiccoloReader/pull/23) |
| App icon + native splash: replace default .NET assets with Pacifico "P" mark | Merged | [#24](https://github.com/fsoftt/PiccoloReader/pull/24) |
| Pinch-zoom fix #2: remove double scale multiplication (zoom-out) | Merged | [#25](https://github.com/fsoftt/PiccoloReader/pull/25) |
| Resume from last-viewed page when reopening a sheet | Merged | [#26](https://github.com/fsoftt/PiccoloReader/pull/26) |

## Plan 3: Icon Annotations

Spec: [`2026-09-14-architecture-design.md`](../specs/2026-09-14-architecture-design.md)
Plan: [`2026-09-15-icon-annotations.md`](2026-09-15-icon-annotations.md)

| # | Task | Status | PR |
|---|---|---|---|
| 1 | Bundle Bravura font + Annotation data model + MusicIconCatalog | Merged | [#28](https://github.com/fsoftt/PiccoloReader/pull/28) |
| 2 | AnnotationService | Merged | [#29](https://github.com/fsoftt/PiccoloReader/pull/29) |
| 3 | Wire annotations into SheetViewerViewModel | Merged | [#31](https://github.com/fsoftt/PiccoloReader/pull/31) |
| 4 | Tool panel UI + Music Icons picker | Merged | [#32](https://github.com/fsoftt/PiccoloReader/pull/32) |
| 5 | Render placed icons on the page | Merged | [#34](https://github.com/fsoftt/PiccoloReader/pull/34) |
| 6 | Select, move, resize, and delete placed icons | Merged | [#35](https://github.com/fsoftt/PiccoloReader/pull/35) |

**Post-merge fixes/polish** (found via testing PR #32 and PR #35):

| Item | Status | PR |
|---|---|---|
| Tool panel polish: bigger icons, tap-outside-to-close | Merged | [#33](https://github.com/fsoftt/PiccoloReader/pull/33) |
| Gesture-arena fix: pinch/pan broken after Task 6 (Tap moved onto PageContainer) | Merged | [#36](https://github.com/fsoftt/PiccoloReader/pull/36) |
| Drag/resize shakiness fix #1: glyph allocation caching + auto-hide handles during drag | Merged | [#37](https://github.com/fsoftt/PiccoloReader/pull/37) |
| Constant-size resize handle + fixed-position drag-to-trash delete (replaces corner delete button) | Merged | [#38](https://github.com/fsoftt/PiccoloReader/pull/38) |
| Drag/resize math fix #2 (zoom-aware normalization), page indicator readability, theme-aware panel/flyout backgrounds, Library empty-label hiding | Merged | [#39](https://github.com/fsoftt/PiccoloReader/pull/39) |

## Housekeeping

| Item | Status | PR |
|---|---|---|
| Pre-commit hook: block direct commits to main | Merged | [#40](https://github.com/fsoftt/PiccoloReader/pull/40) |

## Plan 4: Pencil Annotations + Eraser

Spec: [`2026-09-16-pencil-annotations-eraser-design.md`](../specs/2026-09-16-pencil-annotations-eraser-design.md) ([#41](https://github.com/fsoftt/PiccoloReader/pull/41), revised [#42](https://github.com/fsoftt/PiccoloReader/pull/42))
Plan: [`2026-09-16-pencil-annotations-eraser.md`](2026-09-16-pencil-annotations-eraser.md) ([#43](https://github.com/fsoftt/PiccoloReader/pull/43))

| # | Task | Status | PR |
|---|---|---|---|
| 1 | Annotation data model (Type/ColorHex/StrokeWidth/Points) + AnnotationService.AddStrokeAsync | Merged | [#44](https://github.com/fsoftt/PiccoloReader/pull/44) |
| 2 | StrokeHitTester (eraser point-to-polyline distance) | Merged | [#45](https://github.com/fsoftt/PiccoloReader/pull/45) |
| 3 | Three-tab tool panel (Music Icons/Pencil/Eraser) + ActiveTool + toolbar X | Merged | [#46](https://github.com/fsoftt/PiccoloReader/pull/46) |
| 4 | Pencil: color/width selectors + DrawingView overlay + commit strokes | Merged | [#48](https://github.com/fsoftt/PiccoloReader/pull/48) |
| 5 | Eraser: size selector + drag-to-erase (icons + strokes) + radius indicator | Merged | [#49](https://github.com/fsoftt/PiccoloReader/pull/49) |

Plan 4 complete.

**Post-merge fixes/polish** (found via testing PR #46 and PR #49):

| Item | Status | PR |
|---|---|---|
| Real underline-style tabs (not buttons), theme-aware active/inactive colors, real eraser icon | Merged | [#47](https://github.com/fsoftt/PiccoloReader/pull/47) |
| Eraser drag-to-erase fix: PR #49 shipped tap-only (PointerGestureRecognizer doesn't track touch drags on Android); switched to a repurposed DrawingView for real continuous drag hit-testing | Merged | [#50](https://github.com/fsoftt/PiccoloReader/pull/50) |

## Library Page Fixes

Bounded UI/bug fixes on the Library page, done outside the numbered plans above.

| Item | Status | PR |
|---|---|---|
| Toolbar title "Piccolo" in Pacifico font, independent Folder/Sheet sort buttons (folder sort was previously broken — only root sheets sorted), Folder.DateAdded, multi-select PDF import, blank-gap layout fix when no folders exist | Merged | [#52](https://github.com/fsoftt/PiccoloReader/pull/52) |
| Search icons on Library and Folder pages, hide sort/search controls when the list is empty | Merged | [#54](https://github.com/fsoftt/PiccoloReader/pull/54) |

## Sheet Viewer Polish

Bookmarks, page navigation, and toolbar behavior added/refined on the Sheet Viewer, outside the numbered plans above.

| Item | Status | PR |
|---|---|---|
| Tap-to-toggle toolbar, bookmarks, page jump, tool panel polish | Merged | [#55](https://github.com/fsoftt/PiccoloReader/pull/55) |
| Pencil width preview: fill available panel width | Merged | [#56](https://github.com/fsoftt/PiccoloReader/pull/56) |
| Full toolbar hide/show, tap zones, bookmark names, press-not-release closing, eraser preview | Merged | [#57](https://github.com/fsoftt/PiccoloReader/pull/57) |

## Gesture & Interaction Fixes

Pinch/pan/drag bug fixes affecting the Sheet Viewer's zoom and annotation gestures.

| Item | Status | PR |
|---|---|---|
| Fix compounding pinch-zoom translation drift | Merged | [#58](https://github.com/fsoftt/PiccoloReader/pull/58) |
| Fix Android pinch/pan gesture-arena race | Merged | [#59](https://github.com/fsoftt/PiccoloReader/pull/59) |
| Fix Android icon drag/resize coordinate contamination | Merged | [#60](https://github.com/fsoftt/PiccoloReader/pull/60) |

## Material Design Polish

Flyout, FAB, and toolbar restyling toward Material Design conventions.

| Item | Status | PR |
|---|---|---|
| Add credit footer to flyout menu | Merged | [#61](https://github.com/fsoftt/PiccoloReader/pull/61) |
| Convert Folder page Import button to a Material FAB | Merged | [#62](https://github.com/fsoftt/PiccoloReader/pull/62) |
| Convert Library page Add Folder and Import buttons to a stacked Material FAB pair | Merged | [#63](https://github.com/fsoftt/PiccoloReader/pull/63) |
| Restyle the Shell flyout with a branded header (app icon + wordmark) and a Library menu icon | Merged | [#64](https://github.com/fsoftt/PiccoloReader/pull/64) |
| Convert Sheet Viewer's tool selector into a speed-dial FAB | Merged | [#65](https://github.com/fsoftt/PiccoloReader/pull/65) |
| Add back a dedicated deactivate (X) toolbar icon to the Sheet Viewer | Merged | [#66](https://github.com/fsoftt/PiccoloReader/pull/66) |

## Plan: Multi-language Support

Spec: [`2026-09-18-localization-design.md`](../specs/2026-09-18-localization-design.md)
Plan: [`2026-09-18-localization.md`](../plans/2026-09-18-localization.md)

English + Spanish localization across the whole app (resx-backed `AppStrings`), a Settings page with a language picker (restart required to apply), and first-launch device-locale detection. Includes a fix found via manual verification of the device-locale fallback: `CultureInfo.CurrentUICulture` doesn't sync with the Android device locale on this runtime, so the fallback now reads it via `Java.Util.Locale.Default` instead.

| Item | Status | PR |
|---|---|---|
| Full app localization, Settings page, device-locale fallback (+ Android locale-detection fix) | Merged | [#67](https://github.com/fsoftt/PiccoloReader/pull/67) |

## Housekeeping (continued)

| Item | Status | PR |
|---|---|---|
| Pin a persistent, repo-committed debug keystore so local and CI builds always sign identically (fixes `INSTALL_FAILED_UPDATE_INCOMPATIBLE` when installing a CI-built APK over a locally-built one) | Merged | [#68](https://github.com/fsoftt/PiccoloReader/pull/68) |

## Plan 5: Undo/Redo

Spec: [`2026-09-19-undo-redo-design.md`](../specs/2026-09-19-undo-redo-design.md)
Plan: [`2026-09-19-undo-redo.md`](../plans/2026-09-19-undo-redo.md)

Session-scoped (in-memory, resets per page) undo/redo for annotation edits: place icon, add stroke, move, resize, delete/erase (button or eraser drag, batched into one step per drag).

| Item | Status | PR |
|---|---|---|
| Reversible action types + undo/redo stacks + toolbar buttons | Merged | [#69](https://github.com/fsoftt/PiccoloReader/pull/69) |

## Plan 6: Navigation Polish

Not planned yet — hamburger flyout across folders, final wiring.
