# PiccoloReader — Future Features

Ideas approved for future work, not scheduled yet.

## Half-page turn
On page turn, the bottom half of the screen already shows the top of the next
page while the user finishes reading the top half. Works with swipe, tap and
Bluetooth pedal.

## Face gesture page turn
Use the front camera to detect a head turn or wink to turn pages, as an
alternative to the pedal. Must be opt-in, processed on-device only, and needs
the camera permission. Consider ML Kit face detection on Android.

## Optional cloud sync
Sync library, PDFs and annotations with Google Drive, Dropbox or WebDAV.
Opt-in; the app stays local-first. Builds on the existing external-folder
mirror (`ExternalLibrarySync`).

## In-app theme choice
Light / Dark / System setting in Settings. Today the app always follows the
system theme (`AppThemeBinding`).

## Implementation note
Any implementation must keep I/O, camera processing and sync off the UI thread
(project rule in `CLAUDE.md`).
