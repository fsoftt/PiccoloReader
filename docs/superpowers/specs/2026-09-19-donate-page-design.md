# Donate Page (Ads via AdMob) — Design

## Goal

Add a "Donate" flyout menu item that opens a page explaining the
developer's no-subscriptions philosophy, with a "See ad" button that
shows a rewarded ad as a way to tip, and a small banner ad fixed at
the bottom of that page only. No ads appear anywhere else in the app.

## Scope

- Android only, for now — the rest of the app (PDF rendering, etc.) is
  Android-only today; iOS's `IPdfPageRenderer` is a `NotImplementedException`
  stub. The Donate flyout item is hidden on iOS rather than showing an
  entry point to a feature that can't work there yet.
- No AdMob account exists yet. Everything is wired up against Google's
  official public test ad unit/App IDs, with a documented, one-flag
  swap to production values once the account and real ad units exist.
- No in-app reward system exists (no coins, no premium unlock) — the
  rewarded ad's "reward" callback is informational only. Watching the
  ad *is* the tip; nothing is granted back to the user.

## Library choice

**`Plugin.AdMob`** (MIT, github.com/marius-bughiu/Plugin.AdMob,
NuGet `Plugin.AdMob` 10.0.90) — explicitly built for net10.0-android,
banner + rewarded + free built-in GDPR/UMP consent handling. Chosen
over `Plugin.MauiMTAdmob`/`MarcTron.Admob`, which are also
net10.0-android-compatible but gate GDPR consent behind a paid tier —
worse fit since AdMob policy requires consent handling for EEA users
and this app has no other reason to add a paid dependency. Hand-rolling
a native Android Mobile Ads SDK binding directly was ruled out as
reinventing something these community plugins already solve (YAGNI).

## Architecture

### Package registration

`src/PiccoloReader/PiccoloReader.csproj` gets a `Plugin.AdMob`
`PackageReference`. `MauiProgram.cs`'s builder chain gets `.UseAdMob()`
alongside the existing `.UseMauiCommunityToolkit()` etc. A single line
sets `AdConfig.UseTestAdUnitIds = true` right after — this makes the
plugin substitute Google's official test ad unit IDs automatically, so
the ad unit ID strings used in code can already look like real
production IDs; flipping this one flag to `false` (once the AdMob
account has real ad units) is the entire "go live" step for ad
identifiers.

### `DonateViewModel` (Core) + `DonatePage` (View)

Same MVVM/DI shape as `SettingsViewModel`/`SettingsPage` — constructor
injection, `AddTransient` registration in `MauiProgram.cs`.

`DonateViewModel` takes `IRewardedAdService` (from the plugin) in its
constructor and exposes:

```csharp
[RelayCommand]
private void SeeAd()
{
    IsAdLoading = true;
    _rewardedAdService.OnAdLoaded += HandleAdLoaded;
    _rewardedAdService.OnAdFailedToLoad += HandleAdFailedToLoad;
    _rewardedAdService.PrepareAd();
}

private void HandleAdLoaded(object? sender, EventArgs e)
{
    Unsubscribe();
    IsAdLoading = false;
    _rewardedAdService.ShowAd();
}

private void HandleAdFailedToLoad(object? sender, EventArgs e)
{
    Unsubscribe();
    IsAdLoading = false;
}

private void Unsubscribe()
{
    _rewardedAdService.OnAdLoaded -= HandleAdLoaded;
    _rewardedAdService.OnAdFailedToLoad -= HandleAdFailedToLoad;
}
```

`IsAdLoading` (`[ObservableProperty] bool`) disables the "See ad"
button while the ad is loading, so a slow network connection can't be
mistaken for a broken button via a double-tap. If the ad fails to load
(no connection, no fill, etc.), `IsAdLoading` resets to `false` so the
button becomes tappable again instead of getting stuck disabled
forever — no error message shown, just a silent reset; the user can
simply try again. The plugin's exact failure-event name should be
confirmed against its actual interface (IntelliSense/source) during
implementation — `OnAdFailedToLoad` here names the concept, not a
verified literal API surface. There is no reward grant on ad
completion — no code path needs to run when the user finishes
watching; the ad impression itself is the entire "tip" mechanism.

`DonatePage.xaml` contains: the localized explanation text, the "See
ad" button bound to `SeeAdCommand`/`IsAdLoading`, and the plugin's
`<admob:BannerAd AdUnitId="..." />` control pinned to the bottom of
the page's layout. The banner is scoped entirely to this page's XAML —
no shared layout/shell chrome changes, so no other page can be
affected by it.

### Flyout entry

`AppShell.xaml` gets a new `FlyoutItem` "Donate" between the existing
Library and Settings items, using the classic Material Icons
heart-outline glyph (`&#xE87E;`, "favorite_border") via the same
`FontImageSource`/`MaterialOutlined` pattern every other flyout/toolbar
icon in this app already uses. Its `IsVisible` is bound to a small
static helper (e.g. `PlatformInfo.IsAndroid`) so iOS never shows an
entry point to a feature that isn't wired up there.

### Manifest

`AndroidManifest.xml` gets one new `<meta-data>` entry for the AdMob
**App ID** (distinct from ad unit IDs — this is the app-level
identifier AdMob's SDK reads at startup). Set to Google's public test
App ID (`ca-app-pub-3940256099942544~3347511713`) for now.

## Localization

New `AppStrings` resx keys (English + neutral, Spanish in
`AppStrings.es.resx`), same pattern as every other string in this app:

| Key | English | Spanish |
|---|---|---|
| `DonateMenuItem` | Donate | Donar |
| `DonatePageTitle` | Donate | Donar |
| `DonateExplanation` | "I don't like subscriptions. I built this app to be free because that's the right choice.\n\nIf you'd like to give me a tip, tap the button below — it'll show a short ad." | "No me gustan las suscripciones. Hice esta app gratis porque es lo correcto.\n\nSi quieres darme una propina, toca el botón de abajo — se mostrará un anuncio corto." |
| `SeeAdButton` | See ad | Ver anuncio |

## Testing

- `DonateViewModel`'s `SeeAdCommand` sequencing (prepare → wait for
  loaded → show, and prepare → wait for failure → reset) gets unit
  tests in `PiccoloReader.Core.Tests`, against a fake
  `IRewardedAdService` implementing the plugin's interface — no real
  Android AdMob SDK involved, consistent with every other ViewModel
  test in this project.
- `AppStrings` additions get the same English/Spanish resolution tests
  already used for every other localized string (see
  `AppStringsTests.cs`).
- `DonatePage.xaml` (banner placement, flyout icon, button states) gets
  manual on-device verification — no automated UI tests, matching how
  every other MAUI page in this project has been verified.

## Not code, but worth flagging

Once real ads go live for an actual release, Play Console's **Data
Safety** form and **Ads** declaration need updating (declaring the app
"contains ads" and that it uses the Advertising ID). That's Play
Console paperwork, unrelated to this implementation.
