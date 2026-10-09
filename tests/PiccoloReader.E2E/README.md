# PiccoloReader.E2E

End-to-end UI tests for the Android app: NUnit + Appium 2 (UiAutomator2 driver) driving the real
APK on an emulator. They are **not** part of `ci.yml`; `.github/workflows/e2e.yml` runs them.

Elements are located by `AutomationId` only (MAUI exposes it as the Android resource-id, or the
content-description for toolbar items), so layout redesigns do not break the tests as long as the
ids stay. Native dialogs (action sheets, prompts, the system file picker) have no ids; they are
matched by the app's own `AppStrings` text (tests force the English UI).

## Run locally

1. Build a **self-contained, debuggable** APK (the tests read the app's database and preferences
   with `adb run-as`, and reset it with `pm clear`):

   ```
   dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android -c Debug ^
     -p:EmbedAssembliesIntoApk=true -p:AndroidPackageFormat=apk
   ```

   The APK is `src/PiccoloReader/bin/Debug/net10.0-android/com.fsoftt.piccoloreader-Signed.apk`.

   > **Warning: do not use a Fast Deployment Debug build.** The normal IDE/`dotnet build -t:Run`
   > Debug flow pushes the .NET assemblies next to the app instead of into the APK. `pm clear`
   > (used between tests) deletes them and the app then crashes on launch. Always build with
   > `EmbedAssembliesIntoApk=true` (or Release) for E2E. If you must keep your Fast Deployment
   > install, set `PICCOLO_RESET=none` (no data wipe; tests then share state and may fail).

2. Start an emulator or connect a device (API 34+ recommended; one device only, or set
   `ANDROID_SERIAL`). `adb` must be on `PATH` or under `ANDROID_HOME`.
3. Install Appium once and start it:

   ```
   npm install -g appium@2
   appium driver install uiautomator2
   appium
   ```

4. Run the suite:

   ```
   set PICCOLO_APK=C:\path\to\com.fsoftt.piccoloreader-Signed.apk
   dotnet test tests/PiccoloReader.E2E
   ```

| Variable | Meaning |
| --- | --- |
| `PICCOLO_APK` | (required) APK to install and test |
| `APPIUM_URL` | Appium server, default `http://127.0.0.1:4723` |
| `E2E_ARTIFACTS` | where failure screenshots / UI dumps / logcat go (default `bin/.../e2e-artifacts`) |
| `PICCOLO_RESET` | `none` disables the `pm clear` between tests |
| `PICCOLO_IMPORT_MODE` | `seed` writes the PDF + DB row directly instead of using the system file picker |
| `ANDROID_SERIAL` | adb device to use |

The tests change global emulator settings (rotation lock) and write PDFs to `/sdcard/Download`.

## Layout

- `Infrastructure/` - `AppSession` (driver, reset, prefs), `Adb`, `AppDb` (reads the app SQLite
  via `adb exec-out run-as ... cat`), `Gestures` (tap, long-press, drag, pinch via W3C actions),
  `PdfFactory` (hand-written test PDFs), `Ui` (AutomationId lookup).
- `Screens/` - page objects (`LibraryScreen`, `ViewerScreen`) holding the AutomationIds.
- `Tests/` - the tests. Each starts from a fresh app with one imported 3-page PDF.
