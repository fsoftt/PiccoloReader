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

## Offline, install and backup

- **Offline / PWA**: `wwwroot/manifest.webmanifest` (scope and start URL `/PiccoloReader/app/`, icons incl. maskable, share target,
  `.pdf` file handler). `service-worker.published.js` (published as `service-worker.js`) precaches the app shell and `_framework`
  from the generated `service-worker-assets.js` into a cache named after the build, and serves it for every in-scope
  navigation, so deep links work offline. The SoundFont keeps its own lazy cache and is not precached; the lazy Bravura font is
  cached at runtime after first use. A new build waits in the background and a "new version available" banner offers the reload.
  The worker uses its own scope as base path, so no rewrite is needed besides the `<base href>` one (`index.html` is therefore
  excluded from the hash check). Dev builds (`dotnet run`) use a no-op worker.
- **Install**: Settings shows "Install app" where the browser offers it (Chromium). Safari/iOS: Share > Add to Home Screen.
- **Storage**: Settings shows usage / quota and whether storage is persistent; `navigator.storage.persist()` is requested at start
  and again on the first import, and can be retried from Settings. Firefox and Safari decide on their own.
- **Backup**: Settings > Export library writes a `.zip` (PDFs as `Folder/Title.pdf`, `piccolo-library.json` = Core `LibrarySnapshot`
  with annotations, bookmarks and crops, `piccolo-settings.json` = preferences). Import merges with Core `LibraryRecovery`: sheets
  are matched by `ContentHash` (hashes of older sheets are computed first), annotations and bookmarks by `SyncId`, preferences only
  fill what is not set. The same layout is the Android external folder, so a backup zip can be unpacked there and vice versa.
  The zip is staged in memory while importing, so very large libraries need RAM for the archive plus one PDF at a time.
- **Linked folder** (Chromium desktop/Android only, File System Access API): Settings > Linked folder mirrors PDFs and the
  snapshot into a chosen directory using Core `ExternalLibrarySync` over an `IExternalLibraryStore` (`LinkedFolderService.cs`),
  and merges an existing library found there with `LibraryRecovery`. The handle is kept in IndexedDB; after a restart the browser
  asks again for permission (tap the row). Hidden in Firefox and Safari.
- **Adding PDFs from outside**: drag and drop onto any page, "Open with" for installed PWAs (`launchQueue`), and the Web Share
  Target (Android Chrome, installed PWA): the worker stores shared PDFs in a cache and the app imports them on load.
- **Tutorial**: first launch shows the six-slide guide (Core `TutorialViewModel`); Settings > Show tutorial replays it. Concert mode
  coach marks (library stage) show once; the viewer stage (tap zones) is not implemented on the web.
- The tuner stops listening (page and viewer strip) while the tab is hidden.

Browser support: everything works in current Chrome/Edge. Firefox and Safari run the app, offline shell, backup zip and drag and
drop, but have no linked folder, install prompt (Firefox desktop) or share target.
