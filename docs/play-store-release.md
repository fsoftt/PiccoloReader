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
