// Drives real two-finger pinch and one-finger pan gestures against the
// sheet viewer on a running emulator, via Appium + UiAutomator2 - the only
// way to exercise multi-touch, since `adb shell input` is single-pointer
// only. Screenshots are saved to out/ at each step for manual comparison
// (there's no in-app hook to read back the live scale/translation, so this
// is a visual/manual check, not a pass/fail assertion).
//
// Prerequisites (one-time):
//   npm install -g appium
//   appium driver install uiautomator2
//   appium --address 127.0.0.1 --port 4723   (leave running in another shell)
//   npm install                               (in this directory)
//
// Then with the target APK installed and the emulator running:
//   node zoom-pan-gesture-check.js
//
// APP_ACTIVITY below must match the app's actual launcher activity, which
// includes a crc64 hash that changes with the build - if the script hangs
// on session creation, re-resolve it with:
//   adb shell cmd package resolve-activity --brief com.fsoftt.piccoloreader
const { remote } = require('webdriverio');
const fs = require('fs');
const path = require('path');

const APP_ACTIVITY = 'crc64e68b3bc50f9f719f.MainActivity';
const DEVICE_UDID = 'emulator-5554';

const OUT_DIR = path.join(__dirname, 'out');
fs.mkdirSync(OUT_DIR, { recursive: true });

async function shot(driver, name) {
  const b64 = await driver.takeScreenshot();
  const file = path.join(OUT_DIR, `${name}.png`);
  fs.writeFileSync(file, Buffer.from(b64, 'base64'));
  console.log(`saved ${file}`);
}

async function pinch(driver, cx, cy, startOffset, endOffset, durationMs = 600) {
  // Two-finger pinch. Positive offset delta (end > start) = fingers moving
  // apart = zoom in. Negative = fingers moving together = zoom out.
  await driver.performActions([
    {
      type: 'pointer',
      id: 'finger1',
      parameters: { pointerType: 'touch' },
      actions: [
        { type: 'pointerMove', duration: 0, x: Math.round(cx - startOffset), y: cy },
        { type: 'pointerDown', button: 0 },
        { type: 'pointerMove', duration: durationMs, x: Math.round(cx - endOffset), y: cy },
        { type: 'pointerUp', button: 0 },
      ],
    },
    {
      type: 'pointer',
      id: 'finger2',
      parameters: { pointerType: 'touch' },
      actions: [
        { type: 'pointerMove', duration: 0, x: Math.round(cx + startOffset), y: cy },
        { type: 'pointerDown', button: 0 },
        { type: 'pointerMove', duration: durationMs, x: Math.round(cx + endOffset), y: cy },
        { type: 'pointerUp', button: 0 },
      ],
    },
  ]);
  await driver.releaseActions();
}

async function pan(driver, x, yStart, yEnd, durationMs = 400) {
  await driver.performActions([
    {
      type: 'pointer',
      id: 'finger1',
      parameters: { pointerType: 'touch' },
      actions: [
        { type: 'pointerMove', duration: 0, x, y: yStart },
        { type: 'pointerDown', button: 0 },
        { type: 'pointerMove', duration: durationMs, x, y: yEnd },
        { type: 'pointerUp', button: 0 },
      ],
    },
  ]);
  await driver.releaseActions();
}

async function main() {
  const driver = await remote({
    hostname: '127.0.0.1',
    port: 4723,
    path: '/',
    capabilities: {
      platformName: 'Android',
      'appium:automationName': 'UiAutomator2',
      'appium:udid': DEVICE_UDID,
      'appium:appPackage': 'com.fsoftt.piccoloreader',
      'appium:appActivity': APP_ACTIVITY,
      'appium:noReset': true,
      'appium:newCommandTimeout': 300,
    },
  });

  try {
    await driver.pause(2000);
    await shot(driver, '01-library');

    const { width, height } = await driver.getWindowSize();
    console.log(`screen: ${width}x${height}`);

    // Tap the "test-multipage" sheet row to open the sheet viewer.
    await driver.performActions([
      {
        type: 'pointer',
        id: 'finger1',
        parameters: { pointerType: 'touch' },
        actions: [
          { type: 'pointerMove', duration: 0, x: Math.round(width * 0.5), y: Math.round(height * 0.195) },
          { type: 'pointerDown', button: 0 },
          { type: 'pause', duration: 80 },
          { type: 'pointerUp', button: 0 },
        ],
      },
    ]);
    await driver.releaseActions();
    await driver.pause(1500);
    await shot(driver, '02-sheet-viewer-opened');

    const cx = Math.round(width * 0.5);
    const cy = Math.round(height * 0.45);

    // Three successive zoom-in pinches (identical start/end offsets each
    // time) - each should produce a similarly-sized zoom increase, not a
    // diminishing one; compare 03/04/05 by eye.
    await pinch(driver, cx, cy, 80, 380);
    await driver.pause(1500);
    await shot(driver, '03-after-pinch-1-zoom-in');

    await pinch(driver, cx, cy, 80, 380);
    await driver.pause(1500);
    await shot(driver, '04-after-pinch-2-zoom-in-again');

    await pinch(driver, cx, cy, 80, 380);
    await driver.pause(1500);
    await shot(driver, '05-after-pinch-3-zoom-in-again');

    // Pan while zoomed in - single finger drag, to check the content tracks
    // the finger at a consistent (not slowed-down) rate.
    await pan(driver, cx, Math.round(height * 0.3), Math.round(height * 0.6));
    await driver.pause(1500);
    await shot(driver, '06-after-pan-while-zoomed');

    // Pinch out (zoom out) - fingers moving from 380px to 80px offset.
    await pinch(driver, cx, cy, 380, 80);
    await driver.pause(1500);
    await shot(driver, '07-after-pinch-out-1');

    await pinch(driver, cx, cy, 380, 80);
    await driver.pause(1500);
    await shot(driver, '08-after-pinch-out-2');

    console.log('Done. Screenshots in', OUT_DIR);
  } finally {
    await driver.deleteSession();
  }
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
