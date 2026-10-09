#!/usr/bin/env bash
# Runs inside android-emulator-runner (emulator already booted, adb connected):
# starts Appium, runs the E2E suite, always leaves logs for the artifact upload.
set -uo pipefail

WORKSPACE="${GITHUB_WORKSPACE:-$(pwd)}"
OUT="$WORKSPACE/e2e-out"
mkdir -p "$OUT" "$WORKSPACE/e2e-artifacts"
touch "$WORKSPACE/e2e-started.marker"   # emulator booted: a failure below is a test failure, not a boot flake

adb devices -l
adb shell getprop ro.build.version.release
# Deterministic UI: English locale, no auto-rotate, no animations.
adb shell settings put global window_animation_scale 0
adb shell settings put global transition_animation_scale 0
adb shell settings put global animator_duration_scale 0
adb shell settings put system accelerometer_rotation 0

appium --log-level info --log "$OUT/appium.log" > "$OUT/appium.stdout.log" 2>&1 &
APPIUM_PID=$!
for i in $(seq 1 60); do
  if curl -fs http://127.0.0.1:4723/status > /dev/null; then break; fi
  sleep 1
done
curl -fs http://127.0.0.1:4723/status || { echo "Appium did not start"; cat "$OUT/appium.stdout.log"; exit 1; }

# Keep logcat for the whole run; uploaded when something fails.
adb logcat -c
adb logcat -v time > "$OUT/logcat.txt" 2>&1 &
LOGCAT_PID=$!

FILTER_ARGS=()
if [ -n "${E2E_FILTER:-}" ]; then FILTER_ARGS=(--filter "$E2E_FILTER"); fi

dotnet test "$WORKSPACE/tests/PiccoloReader.E2E/PiccoloReader.E2E.csproj" -c Release --no-build "${FILTER_ARGS[@]}" \
  --logger "trx;LogFileName=e2e.trx" --results-directory "$OUT/results" \
  --logger "console;verbosity=normal"
RESULT=$?

kill "$LOGCAT_PID" 2>/dev/null || true
kill "$APPIUM_PID" 2>/dev/null || true
exit $RESULT
