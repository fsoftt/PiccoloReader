# Piano — design

Date: 2026-11-02
Status: approved decisions (sound source, placement, extras) from the user; details below.

## Goal

A playable virtual keyboard for musicians of any instrument: check a pitch, hear an
interval, try a phrase with the sound of their own instrument. Two entry points:

1. A new bottom tab **Piano** (Android only, like Tuner).
2. A compact keyboard strip in the sheet viewer, toggled from the reading bar, so the
   reader can play a note without leaving the score.

## Decisions

| Topic | Decision |
|---|---|
| Sound | MeltySynth 2.4.1 (MIT, netstandard2.1) + SoundFont subset of MuseScore_General (MIT), trimmed by our own `tools/sf2trim`, asset ≤ 8 MB (hard max 10 MB). |
| Placement | 5th tab `piano` + sheet viewer strip (phase 2). |
| Extras v1 | Note labels, sustain pedal, glissando + multi-touch chords. |
| Out of scope v1 | Transposing notation, recording/playback, MIDI in/out, iOS audio, haptics, metronome sync. |

## Instruments

Catalog in Core (`PianoInstruments.All`), grouped by family, in this order:

- Keyboards: Grand piano (GM 0), Electric piano (4), Church organ (19)
- Woodwinds: Piccolo (72), Flute (73), Oboe (68), Clarinet (71), Alto sax (65), Tenor sax (66)
- Brass: Trumpet (56), Trombone (57), French horn (60)
- Strings: Violin (40), Cello (42), String ensemble (48), Nylon guitar (24)
- Voices: Choir (52)

Each entry: `Id` (stable string, persisted), `GmProgram`, `NameKey` (AppStrings),
`Family`, `LowNote`/`HighNote` (written-sounding range of the real instrument, MIDI),
`Gain` (loudness normalisation measured from the trimmed SoundFont).

Gain per GM program (equalises first-second RMS of the trimmed asset; sounds approved
by the user 2026-11-02): 0: 5.0, 4: 1.0, 19: 1.39, 24: 2.18, 40: 1.13, 42: 0.87,
48: 1.27, 52: 1.44, 56: 1.28, 57: 0.42, 60: 0.81, 65: 0.84, 66: 0.94, 68: 0.94,
71: 0.97, 72: 0.87, 73: 1.0. Master gain 1.69 keeps the loudest preset at -3 dBFS
for one note; the engine adds a soft limiter (tanh-style) so chords never clip.

Asset trade-offs (accepted): mono, one velocity layer, piano notes ring 2–3.5 s max,
organ/strings/choir at 8 kHz. Asset 7,870,890 bytes.

Keys outside the instrument range stay playable but are drawn muted, and the
mini-map shows the range as a bracket. This tells a flautist where their instrument
ends without blocking anyone. The app is for all instruments: never limit the keyboard
range to one instrument.

## Screen: Piano tab

Portrait, top to bottom:

1. **Header**: page title, and the instrument chip (`[icon] Grand piano ▾`). Tap opens a
   bottom-sheet popup (CommunityToolkit.Maui `Popup`) with the grouped list; the current
   instrument has a check mark. Selection applies immediately (notes in flight are
   released) and closes the sheet.
2. **Note readout** (centred in the flexible space between header and controls, 68 sp): large name(s) of the sounding notes, e.g. `La4` or `Do4 · Mi4 · Sol4`
   (max 4, low to high). The last note stays dimmed after release so the user can read
   it. Naming follows the existing tuner `NoteNaming` preference (Auto/Letter/Solfege),
   so the whole app speaks one notation.
3. **Mini-map**: the full 88 keys (A0–C8) as a thin strip (~32 dp) with a highlighted
   window for the visible range. Drag the window or tap to jump. Pressed keys and the
   instrument range also show here.
4. **Control row** (grouped with the mini-map and keyboard at the bottom, 8-12 dp above
   the keys): `‹ C4–E5 ›` shifts one octave (clamped to A0–C8) and shows the visible range
   (naming from the tuner preference, e.g. `Do4–Mi5`); key-count stepper `− 14 keys +`
   (white keys, 7–36). Second line in portrait: two half-width chips, sustain (`Chip` when
   off, `ChipSelected` Primary tint when on) and labels (always neutral `Chip`, showing its
   mode as text: `–`, `C`, `C D E` / `Do Re Mi`) cycling Off → C only → All.
5. **Keyboard**: anchored to the bottom; in portrait its height is 44 % of the page
   (min 260 dp), so keys stay big and near the thumb.

Landscape: header + control row merge into one row (sustain icon-only); mini-map above the keyboard;
the keyboard takes all remaining height.

Key count is stored per orientation (portrait default 10 white keys = C4–E5,
landscape default 17 = C3–E5), because the comfortable count differs a lot.

### Keyboard visuals (SkiaSharp `SKCanvasView`)

- White keys: soft vertical gradient, 1 dp gaps, 4 dp rounded bottom corners. Dark
  theme uses off-white (#E6E6EE family) to avoid glare on a dark page.
- Black keys: 62 % height, 58 % of white width, dark gradient with a thin top
  highlight.
- Pressed: fill tinted with `Primary` (light #5B3FD9 / dark #8B6CFF), key drawn 1 dp
  lower, black key highlight removed.
- Muted (out of instrument range): lower contrast overlay.
- Labels at the bottom of white keys (Caption size). "C only" mode shows `Do4`/`C4` on
  C keys, bold; "All" mode shows names on white keys and octave only on C.
- Loading state: keys drawn at 40 % opacity with "Loading sounds…" until the SoundFont
  is ready; touches ignored.
- Draw only on state change (press, release, scroll, resize, theme), no animation loop.
  Paints and paths cached; no per-frame allocation.

### Touch

- `EnableTouchEvents`; map `pointerId → note`, so chords and several fingers work.
- Pressed: hit-test (black keys first) → NoteOn. Moved: if the note under the finger
  changes → NoteOff old, NoteOn new (glissando). Released/Cancelled: NoteOff. Always
  `e.Handled = true`.
- Velocity from the touch position on the key: near the front edge = louder
  (range 70–120), like a real key pressed at the tip vs the root.
- The touch handler runs on the UI thread and only enqueues commands; no audio work.

## Audio architecture

```
Core (testable)                          Android
───────────────                          ───────
PianoInstruments  (catalog)              AndroidPianoAudioOutput : IPianoAudioOutput
PianoKeyboardLayout (geometry, hit test)   AudioTrack PCM float stereo, LowLatency,
PianoSynthEngine  (MeltySynth wrapper)      buffer = max(minBuffer, 4 bursts),
  - command queue (ConcurrentQueue)         Usage Media / ContentType Music,
  - Render(float[] l, float[] r)            dedicated thread, Audio priority,
PianoService (ref-counted session)          pulls engine.Render per block
IPianoPreferenceService
PianoViewModel / PianoStripViewModel
```

- **PianoSynthEngine** owns the MeltySynth `Synthesizer` (channel 0). NoteOn/NoteOff/
  program change/CC64 are enqueued from any thread and applied by the audio thread at
  the start of each render block, so the synthesizer is touched by one thread only (no
  lock on the hot path). Polyphony is capped at 32 voices. When no voice is sounding, no command
  is queued and the reverb tail (1.5 s) is over, `Render` returns false without running the
  synthesizer and the output keeps writing a zeroed buffer (idle CPU about 1 %; the track is not
  stopped, to keep latency). Block size 128–256 frames → command latency ≤ ~5 ms on top of
  the device output latency.
- **SoundFont load**: on first acquire, `Task.Run` → `FileSystem.OpenAppPackageFileAsync
  ("piano.sf2")` → `new SoundFont(stream)` (a non-seekable asset stream goes through one pre-sized buffer that is freed and collected right after parsing). Loaded once and kept
  for the process. Never on the UI thread (CLAUDE.md rule).
- **Failure handling**: the service keeps the sustain flag (source of truth) and re-applies it
  after the engine is published and after every start-time silence (stream start, Suspend,
  Resume). If the audio thread dies (write error, render exception) the output raises `Died`
  and the service restarts the stream, at most once per second; a note pressed while the stream
  is down also triggers a throttled restart and is dropped (the command queue cannot grow). A
  stale thread that outlived its join timeout cannot render: renders are serialised and checked
  against the stop flag. A stream that cannot start sets `PianoLoadState.Failed`; the keyboard
  shows "Sound unavailable" and the next `Acquire` retries (without reloading the SoundFont).
  Preferences are written on the thread pool.
- **PianoService** is a singleton with `Acquire()`/`Release()` (tab page and viewer strip
  each hold a lease while visible). The audio stream starts on the first lease and stops
  2 s after the last release, so switching tab ↔ viewer does not rebuild the track.
  Window Stopped → all notes off, stream stopped; Resumed → restarted if leases remain.
- **No audio focus request**: the metronome stops on focus loss; the musician must be
  able to play the piano over a running metronome. The piano is user-driven and silent
  when idle, so it does not need focus.
- **iOS**: `UnsupportedPianoAudioOutput`; tab and strip hidden via
  `PlatformInfo.IsAndroid`, same as Tuner.

## Preferences (`MauiPianoPreferenceService`, keys prefixed `Piano`)

`PianoInstrument` (Id), `PianoWhiteKeysPortrait`, `PianoWhiteKeysLandscape`,
`PianoFirstWhiteKey` (MIDI), `PianoLabels` (Off/COnly/All), `PianoStripWhiteKeys`,
`PianoStripFirstWhiteKey`. Sustain is not persisted (always starts off).
The instrument is shared between tab and strip.

## Sheet viewer strip (phase 2)

- Reading bar button `ViewerPianoStripButton` next to the tuner strip button, Android
  only, icon tinted Primary when open.
- Panel overlays the bottom of the page (does not resize the PDF viewport, which would
  re-layout pages): drag handle, compact row (instrument chip, `‹ ›`, close), keyboard
  default 14 white keys. Height ~150 dp portrait, ~120 dp landscape.
- Touches on the strip never reach the page. Pedal page turn keeps working. The strip
  holds a `PianoService` lease while open and visible.

## Licences and credits

- `src/PiccoloReader/Resources/Raw/piano.sf2` (generated, committed).
- `tools/sf2trim/` committed: one command rebuilds the asset from the upstream URL,
  verified by SHA-256; documents the preset list and options.
- `licenses/MuseScore_General-LICENSE.md` (full MIT text, all copyright holders, note
  "modified subset") and `licenses/MeltySynth-LICENSE.txt`.
- Settings → Legal credit links: MuseScore General SoundFont, MeltySynth.
- F-Droid metadata licence stays GPL-3.0-only.

## Testing

- Core unit tests: layout geometry and hit test (black over white, edges, gaps); octave
  shift and key-count clamps; catalog (unique ids, ranges valid, every GM program
  present in the asset); engine offline render (NoteOn → non-silent, NoteOff + release
  → silent, sustain holds, instrument change releases notes); view model (labels cycle,
  per-orientation key count, persistence via fake preference service).
- E2E (Appium): Piano tab opens, key-count stepper changes the label, instrument sheet
  selects an instrument.
- Manual on emulator: sound for each instrument, chords, glissando, latency feel, piano
  over running metronome, dark theme, landscape.

## Delivery

- PR 1: asset + tool + licences, Core engine/layout/catalog/view model, Android output,
  Piano tab, settings credits, strings en/es, tests, docs/requirements.md.
- PR 2: sheet viewer strip.
- Release later via the normal release process.
