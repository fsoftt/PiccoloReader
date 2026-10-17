# Project instructions

## Coding rules

- Never block the UI thread. File I/O, image decode/encode (SkiaSharp), PDF rendering and database access run asynchronously and off the UI thread (async APIs or Task.Run). Do not use .Wait(), .Result or synchronous I/O in view models, page lifecycle handlers (OnAppearing/OnDisappearing) or event handlers. Fire-and-forget work on navigation must not delay the navigation.
