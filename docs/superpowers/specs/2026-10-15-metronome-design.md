# Metronome — Design

## Goal

Add a "Metronome" tab with a click track the musician can run while
reading: tempo, beats per bar, accent, subdivision, tap tempo and a
beat indicator. It keeps playing while navigating anywhere in the app
(including the sheet viewer, which gets a small toggle) and stops when
the app goes to the background.

Performance is the top priority: the metronome must cost nothing when
stopped and almost nothing when running, and must never cause UI jank.

## Scope

- Android only. iOS must keep compiling: it registers
  `UnsupportedMetronomePlayer`, a no-op that never plays.
- Stage 2 (not part of this work): a **tuner**. It will be active only
  inside its own screen (mic capture starts on appearing and is released
  on disappearing), unlike the metronome, which is app-wide. The player
  abstraction here deliberately does not assume a UI owner so the tuner
  can follow the opposite lifetime without touching it.
- No sound asset files and no SoundPool: clicks are synthesized.

## Behavior

| Setting | Range | Default |
|---|---|---|
| BPM | 30–250 | 100 |
| Beats per bar | 1–12 | 4 |
| Accent first beat | on/off | on |
| Subdivision | 1 (none), 2, 3, 4 clicks per beat | 1 |

- Subdivision clicks are quieter (about 50% amplitude) and never accented.
- **Tap tempo**: BPM from the average of the last (up to) 4 intervals;
  the series restarts when the gap between taps exceeds 2 s; the result is
  clamped to the BPM range.
- Settings are persisted (not the playing state). Changes made while
  playing apply from the next beat, with no restart or glitch.
- Playback stops when the app window stops (goes to background). Audio
  focus loss is not handled.

## Architecture

Follows the existing split: pure logic and interfaces in
`PiccoloReader.Core` (unit tested), platform code in the App project.

### Core (`PiccoloReader.Core.Services.Metronome`)

- `MetronomeSettings` — immutable record; every property clamps in its
  `init` accessor, so snapshots are always valid (also via `with`).
- `ClickSynth` / `ClickSet` — PCM16 clicks built once: a 25 ms sine with
  an exponential decay; normal 1000 Hz, accent 1500 Hz, subdivision
  1000 Hz at half amplitude.
- `MetronomeScheduler` — the sample-exact part. `Fill(buffer, frames,
  settings)` renders the next chunk of the stream. It keeps the absolute
  frame index and places clicks by it, carrying a click that straddles a
  chunk boundary into the next chunk. Beat positions come from an anchor
  frame and exact integer math (`anchor + n * sampleRate * 60 / bpm`), so
  rounding never accumulates and tempo cannot drift. The settings snapshot
  is read when a beat begins, which is what makes changes apply from the
  next beat; a BPM change re-anchors at that beat. It also reports the
  main beats that start inside the chunk (`BeatEvents`) in preallocated
  storage, so it is allocation free after construction.
- `TapTempo` — timestamps in, BPM out.
- `IMetronomePlayer` — `Start`, `Stop`, `IsPlaying`, `UpdateSettings`,
  `BeatTicked(beatInBar, isAccent)`, `IsPlayingChanged`.
- `IMetronomePreferenceService` — `Load`/`Save` of `MetronomeSettings`.
- `MetronomeViewModel` (singleton) — settings, commands (`TogglePlay`,
  `IncrementBpm`, `DecrementBpm`, `Tap`), `CurrentBeat` for the indicator.
  `Attach()`/`Detach()` subscribe/unsubscribe the beat events; settings are
  saved on `Detach()` and when playback stops, only if they changed.

### App project

- `AndroidMetronomePlayer` (`Platforms/Android`, DI singleton, independent
  of any page).
  - **Zero work when stopped**: `Start()` creates a session (a thread, an
    `AudioTrack`, one chunk buffer); `Stop()` flags it and the thread
    stops and releases the track and exits. No timers or idle thread.
  - One `AudioTrack` in `MODE_STREAM`, PCM16 mono, at
    `AudioTrack.GetNativeOutputSampleRate(Stream.Music)` (48000 fallback),
    `Usage.Media` + `ContentType.Sonification`, low-latency performance
    mode on API 26+, buffer of three chunks.
  - One background thread at `ThreadPriority.Audio` loops: `Fill` a chunk
    (20 ms, one reused `short[]`), blocking `Write`. The blocking write is
    the only clock; there is no `Timer`/`Task.Delay`. No allocations,
    logging or contended locks in the loop.
  - Settings travel through a `volatile` reference to the immutable
    record.
- `MauiMetronomePreferenceService` — `Preferences.Default`, same pattern
  as `MauiReadingPreferenceService`.
- `UnsupportedMetronomePlayer` — iOS.

### Beat indicator latency compensation

`Fill` runs ahead of what is audible by roughly the track buffer. The
player keeps a small preallocated ring of beats that were written but not
yet heard (absolute frame, bar index, accent). After each chunk write
(the write blocks once the buffer is full, so this runs about once per
chunk) it reads `AudioTrack.PlaybackHeadPosition` and raises
`BeatTicked` for every pending beat whose frame the head has reached,
using unsigned wrap-safe comparison. So the dot lights when the beat is
played, within one chunk (about 20 ms). Output-device latency beyond the
playback head is not compensated. The event is dispatched to the main
thread only while someone is subscribed.

### Lifecycle

- `App.CreateWindow` hooks `Window.Stopped` (activity stopped, i.e. the
  app is in the background) to stop the player and persist pending
  settings. The player is a singleton, so navigating between pages never
  affects it.
- `MetronomePage` subscribes to the view model in `OnAppearing` and
  unsubscribes in `OnDisappearing` (`Attach`/`Detach`), so a hidden page
  causes no UI work.

## UI

- New Shell tab "Metronome" / "Metrónomo" before Settings, timer glyph.
- `MetronomePage`: big BPM number, −/+ buttons, `Slider`, Tap button,
  big Play/Stop button, a row of beat dots (the accented first beat is
  tinted and drawn larger while lit), and a card with rows for beats per
  bar and subdivision (action sheets, as the Settings language row does)
  and the accent switch. No animations; dots are plain `Border`s whose
  color changes.
- `SheetViewerPage` reading bar gets a metronome toggle button (timer icon
  when stopped, tinted crossed-timer icon while playing).

## Testing

`PiccoloReader.Core.Tests`: scheduler (no drift over 10 minutes at
120 BPM / 48 kHz and at a non-divisible tempo, identical placement for
different chunk sizes, click split across a chunk boundary, BPM /
subdivision / bar changes applying from the next beat, accent and
subdivision placement, beat events), `MetronomeSettings` clamping,
`TapTempo`, `ClickSynth`, and `MetronomeViewModel` with fake player and
preferences. Audio output and lifecycle are verified manually on the
Android emulator.
