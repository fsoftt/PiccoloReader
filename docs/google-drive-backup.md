# Google Drive backup

Optional backup/restore of the library to the user's own Google Drive (issue #111).
Visible folder `PiccoloReader/` in the user's Drive, OAuth scope `https://www.googleapis.com/auth/drive.file` only
(the app only sees files it created).

## Status / next steps (feature paused, branch `feat/drive-backup`)

Done:
- Core (`src/PiccoloReader.Core/Services/Backup`): `IDriveClient` + `GoogleDriveClient` (Drive REST v3, resumable upload),
  `BackupService` (incremental PDF upload by SHA-256 kept in Drive `appProperties`, JSON snapshot `piccolo-library.json`,
  `backup-info.json`, automatic-backup runner), `BackupPlanner`, `DriveTokenProvider`, `BackupSettingsViewModel`,
  en/es strings. 358 Core tests pass (fake Drive, HTTP stub).
- Android: `AndroidGoogleAuthService` (Identity AuthorizationClient, consent result handled in `MainActivity.OnActivityResult`),
  `BackupWorker` + `AndroidAutoBackupScheduler` (WorkManager, 24 h, unmetered), DI wiring, `MauiBackupStateStore`.
  Builds with 0 errors (also `-p:PiccoloQa=true`). NOT tested on a device (OAuth clients do not exist yet).
- `SyncId` (GUID) column added to `Annotation` and `Bookmark` with startup backfill (groundwork for merge).

Remaining:
1. **Restore must MERGE, never delete local data** (decision after the first implementation, which still does
   replace + safety copy in `BackupService.RestoreCoreAsync` and must be rewritten):
   - Restore brings Drive content into the app without removing anything; the next backup (or the same operation)
     uploads what exists only in the app, so Drive becomes the union (two-way union sync).
   - Sheets match by PDF content hash (same PDF on two devices = one sheet). Folders match by name (create missing).
     A sheet that exists locally keeps its local folder; one only in Drive uses Drive's folder.
   - Annotations and bookmarks: union per sheet/page, deduplicated by `SyncId`. Page crops: keep local, else take Drive's.
   - Preferences: keep local values (do not overwrite reading mode, language, ads on restore).
   - Deletions are NOT propagated either way; say so in the UI caption and here.
   - Backup must not delete Drive files of sheets removed locally (drop orphan removal in `BackupPlanner`), and should
     union the remote snapshot into the uploaded snapshot so another device's data is not lost; the planner should treat a
     remote file with the same hash (any key) as already uploaded, and restore should resolve files by key, then by hash.
   - Tests: idempotent (restore twice = no duplicates), union of two devices, same PDF on both, SyncId dedupe,
     folder/crop/preference rules. Update the existing replace-based restore tests and the strings/confirm dialog.
2. Settings UI: "Backup" section in `SettingsPage.xaml` (rows with AutomationIds `SettingsBackupAccountRow`,
   `SettingsBackupNowRow`, `SettingsRestoreRow`, `SettingsAutoBackupSwitch`, bound to `BackupSettingsViewModel`,
   progress, simple confirm dialog, "last backup" caption), reload the library after restore (`BackupService.LibraryRestored`).
3. Update `docs/privacy-policy.md` and `docs/es/privacy-policy.md` (optional backup to the user's own Drive, `drive.file` only).
4. Create the OAuth clients below and test on a device (consent flow, silent re-authorization in the worker, restore on a fresh install).

## Drive layout

```
PiccoloReader/
  Partituras/<title>.pdf     one file per sheet; appProperties: piccoloKind=sheet, piccoloFile=<local file>, sha256=<hash>
  piccolo-library.json       snapshot: folders, sheets, annotations, bookmarks, page crops, preferences
  backup-info.json           date, app version, counts
```

Files are found by `appProperties`, so renaming or moving them in Drive is fine.

## Google Cloud setup (needed by the app owner)

1. Create a Google Cloud project and enable the **Google Drive API**.
2. OAuth consent screen: User type **External**, add scope `.../auth/drive.file`, then **publish to production**
   (`drive.file` is non-sensitive, no verification needed).
3. Create two **Android** OAuth client IDs:
   - Package `com.fsoftt.piccoloreader`, with the SHA-1 of the **Play App Signing** key and of the **upload** key
     (Play Console, App integrity). One client per SHA-1.
   - Package `com.fsoftt.piccoloreader.qa`, SHA-1 of the debug keystore:
     `11:33:4C:AA:6A:90:35:CE:07:E8:D2:07:8E:8E:32:25:5E:22:1B:A9`.
4. No client ID goes in the code. Local debug builds and the E2E/CI builds use the committed debug keystore.

Until the clients exist, "Connect" fails with a developer error and the app shows a friendly "not set up" message.
