# Play Store Release Pipeline

## Two separate keystores

- `src/PiccoloReader/debug.keystore` (committed to the repo) — signs
  every internal Debug/Release build (`build-apk.yml`, local builds).
  It's intentionally shared/public: its only job is making sure any two
  builds of this app can install over each other without a signature
  mismatch. Never use it for anything published to end users.
- The Play Store release keystore (`piccoloreader-release.keystore`) —
  lives only on the machine(s) that generated it and in the GitHub
  secrets below. Never committed anywhere. If it's lost, recovering
  publish access requires Google's key-reset support process (days of
  delay) even under Play App Signing.

## Why the workflow only triggers on `release`

`.github/workflows/publish-play-store.yml` fires on `push` to the
`release` branch, not on `pull_request`. GitHub Actions never exposes
repository secrets to workflows triggered by a fork's pull request, so
even after this repo goes open source, an external contributor's PR
can't reach these secrets — the publish workflow only runs once their
code has already been reviewed and merged into `release` by a
maintainer. Two things keep that true going forward:

- Keep real GitHub branch protection enabled on `main` and `release`
  (Settings → Branches: require a reviewed PR, no direct pushes) — the
  local pre-commit hook alone isn't a substitute for this.
- Pin any third-party Action used in this workflow to a commit SHA
  (already done for `r0adkll/upload-google-play`), not a mutable tag,
  so a compromised upstream release can't silently change what runs.

## Required GitHub secrets

Set these under Settings → Secrets and variables → Actions:

| Secret | Value |
|---|---|
| `PLAY_KEYSTORE_BASE64` | `base64 -w0 piccoloreader-release.keystore` (the whole file, base64-encoded) |
| `PLAY_KEYSTORE_PASSWORD` | The keystore's store/key password (same value for both - PKCS12) |
| `PLAY_SERVICE_ACCOUNT_JSON` | The full JSON key for a Google Play service account with Release Manager access to this app, scoped to nothing else |

The service account is created in Google Cloud Console and linked to
the Play Console app under Setup → API access. It needs no permissions
beyond releasing this one app.

## Current status

Not yet wired up end-to-end: there's no Google Play Developer account
or Play Console app entry for PiccoloReader yet, so the secrets above
aren't set and this workflow has never actually run. It's ready to go
once that account/app exists — create the `release` branch and add the
three secrets, and the next push to `release` builds and uploads.

## Track

Defaults to the `internal` track. A brand new Play Store listing can't
go straight to `production` — Google requires a closed test with 12+
testers over 14 continuous days first. Promote through Play Console's
UI once that requirement is met; extending this workflow to also push
to `production` isn't worth doing until then.

## Pending

Play Console shows two warnings on every uploaded App Bundle. Neither
blocks a release; both only make crash and ANR reports harder to read.

- **No deobfuscation file.** When R8/ProGuard shrinks and renames the
  Java side of the app (AndroidX, MAUI's Java glue), Play needs the
  `mapping.txt` it produced to turn renamed stack frames back into real
  class and method names. To do: check whether the Release build runs R8
  (look for `mapping.txt` under `obj/Release`). If it does, upload that
  file with each bundle (the upload step in the release workflow can
  attach it). If it doesn't, the warning is generic and can be ignored.
  (2026-10-14: a local QA Release build produced no `mapping.txt`, so R8
  does not seem to run; confirm on the CI bundle build.)
- **No native debug symbols.** The bundle ships native libraries (.NET
  runtime, SkiaSharp, the PDF renderer). A crash inside one shows only
  memory addresses unless a `native-debug-symbols.zip` (the unstripped
  `.so` files per ABI) is uploaded. To do: see whether the .NET Android
  build can keep unstripped libraries, zip them per ABI and upload them
  with the bundle. Low priority: crashes in our own C# code are managed
  exceptions and don't need these symbols.
