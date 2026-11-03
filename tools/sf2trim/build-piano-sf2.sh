#!/bin/sh
# Rebuilds src/PiccoloReader/Resources/Raw/piano.sf2 from the upstream MuseScore_General.sf2.
# usage: tools/sf2trim/build-piano-sf2.sh [path/to/MuseScore_General.sf2]
# Without an argument the upstream file is downloaded and verified by SHA-256.
# Programs: piano0 EP4 organ19 nylon24 violin40 cello42 strings48 choir52 trumpet56 trombone57 horn60 altoSax65 tenorSax66 oboe68 clarinet71 piccolo72 flute73
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$HERE/../../src/PiccoloReader/Resources/Raw/piano.sf2"
URL="https://ftp.osuosl.org/pub/musescore/soundfont/MuseScore_General/MuseScore_General.sf2"
SHA="ee51d2c4b1525e70f19a45909c4fd7a2e26d91d115fa89dbf5a6bc413d8b9bf3"
IN="$1"
if [ -z "$IN" ]; then
  IN="$(mktemp -d)/MuseScore_General.sf2"
  curl -fL -o "$IN" "$URL"
fi
echo "$SHA  $IN" | sha256sum -c -
dotnet run -c Release --project "$HERE" -- "$IN" "$OUT" \
  --programs 0,4,19,24,40,42,48,52,56,57,60,65,66,68,71,72,73 \
  --mono --vel 64 --cut-looped 0 --maxlen-prog 0=3.5:2 \
  --thin-prog 0=2,4=2,19=3,48=3,52=3,60=2,66=2 \
  --rate 16000 --rate-prog 0=22050,4=11025,19=8000,48=8000,52=8000,66=11025
