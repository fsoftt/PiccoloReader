# Contributing

## Branching

`main` is protected **by convention** (GitHub free-tier private repos don't
support branch protection rules/rulesets — confirmed 2026-09-14, both the
classic branch-protection API and the rulesets API return 403 asking to
upgrade to GitHub Pro or make the repo public).

Rules:

- No direct commits to `main`, except initial repo scaffolding.
- All other work happens on a feature branch, merged into `main` via a
  pull request.

This is not technically enforced by GitHub — it relies on whoever is
committing (human or agent) following it.

## Coding rules

- Never block the UI thread. File I/O, image decode/encode (SkiaSharp), PDF rendering and database access run asynchronously and off the UI thread (async APIs or Task.Run). Do not use .Wait(), .Result or synchronous I/O in view models, page lifecycle handlers (OnAppearing/OnDisappearing) or event handlers. Fire-and-forget work on navigation must not delay the navigation.
