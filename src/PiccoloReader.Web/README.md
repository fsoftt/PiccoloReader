# PiccoloReader.Web (spike)

Blazor WebAssembly app reusing `PiccoloReader.Core` (AppDatabase / sqlite-net, preference interfaces).

- PDF rendering: vendored pdf.js (`wwwroot/lib/pdfjs`, version in `VERSION.txt`) drawing to a canvas (`wwwroot/js/pdf.js`).
- PDFs: stored in OPFS under `sheets/`. DB: SQLite file in the WASM in-memory FS, restored from and debounce-saved to OPFS (`Services/PersistentDatabase.cs`).
- Preferences: `ILanguagePreferenceService` backed by localStorage.
- Build needs the `wasm-tools` workload (`dotnet workload install wasm-tools`) because native e_sqlite3 is linked in.

## Base path

`wwwroot/index.html` has `<base href="/" />` for local dev (`dotnet run --project src/PiccoloReader.Web`).
The Pages workflow rewrites it to `/PiccoloReader/app/` after publish. To test a subpath locally,
publish and edit the copied `index.html` the same way, then serve the folder under that path.
Deep links on Pages go through `docs/404.html`, which bounces to the app shell.
