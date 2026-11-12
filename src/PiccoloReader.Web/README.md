# PiccoloReader.Web

Blazor WebAssembly app reusing `PiccoloReader.Core` (AppDatabase / sqlite-net, view models, services, AppStrings resources).

- Screens mirror the Android app: Library (folders, sort, search, multi-file import, thumbnails), Folder, Viewer
  (horizontal / vertical paged / continuous, zoom, per-page crop, bookmarks, tap zones, swipe, keyboard and pedal keys,
  concert mode), Settings (language, reading mode, concert mode, legal links).
- Reused from Core: `LibraryViewModel`, `FolderViewModel`, `LibraryService`, `BookmarkService`, `PageCropService`,
  `PageCrop`, `PedalKeyMap`, `SheetSorter`, `ConcertQueue`, `AppStrings` (en/es).
- PDF rendering: vendored pdf.js (`wwwroot/lib/pdfjs`, version in `VERSION.txt`) drawing to a canvas (`wwwroot/js/pdf.js`, `wwwroot/js/viewer.js`).
- PDFs and thumbnails: OPFS under `sheets/` and `thumbs/`. DB: SQLite file in the WASM in-memory FS, restored from and
  debounce-saved to OPFS (`Services/PersistentDatabase.cs`); flushed immediately on `visibilitychange` / `pagehide`.
- Preferences (`Services/LocalStoragePreferences.cs`): language, reading mode, sort and concert mode over localStorage.
  A language change reloads the page, like the Android restart prompt.
- Look: `wwwroot/css/tokens.css` holds the design tokens taken from the MAUI `Colors.xaml` / `Styles.xaml` (light and dark,
  following the system theme). Fonts (Open Sans, Pacifico) and the Material Symbols icon font are the same files as the app and
  are served locally; icons use the same glyph code points as the XAML.
- Build needs the `wasm-tools` workload (`dotnet workload install wasm-tools`) because native e_sqlite3 is linked in.

## Base path

`wwwroot/index.html` has `<base href="/" />` for local dev (`dotnet run --project src/PiccoloReader.Web`).
The Pages workflow rewrites it to `/PiccoloReader/app/` after publish. To test a subpath locally,
publish and edit the copied `index.html` the same way, then serve the folder under that path.
Deep links on Pages go through `docs/404.html`, which bounces to the app shell.
