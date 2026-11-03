# sf2trim

Builds the piano SoundFont shipped with the app (`src/PiccoloReader/Resources/Raw/piano.sf2`,
7,870,890 bytes) from MuseScore General: keeps 17 General MIDI presets (bank 0), converts
to mono, one velocity layer, trims long notes and lowers the sample rate per preset.

- Upstream: https://ftp.osuosl.org/pub/musescore/soundfont/MuseScore_General/MuseScore_General.sf2
- SHA-256: `ee51d2c4b1525e70f19a45909c4fd7a2e26d91d115fa89dbf5a6bc413d8b9bf3`
- Rebuild (downloads and verifies the upstream file): `sh tools/sf2trim/build-piano-sf2.sh`
- Licence of the result: MIT, see `licenses/MuseScore_General-LICENSE.md`.

This project is a standalone tool: it is not referenced by the app or by `PiccoloReader.slnx`.
