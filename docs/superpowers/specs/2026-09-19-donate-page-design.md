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
alongside the existing `.UseMauiCommunityToolkit()` etc., passing
`androidDefaultBannerAdUnitId`/`androidDefaultRewardedAdUnitId`
placeholder values — this is the *one* place ad unit IDs are
configured; `MauiDonateAdService.CreateAd()` and `DonatePage`'s
`<admob:BannerAd>` both omit an explicit ad unit ID and fall back to
these defaults (confirmed from the plugin's own
`RewardedAdService.GetAdUnitId`/`BannerAdHandler.GetAdUnitId`, both of
which fall back to `AdConfig.Default...AdUnitId` when none is passed
explicitly). A single line right after sets
`AdConfig.UseTestAdUnitIds = true`, which makes the plugin substitute
Google's official test ad unit IDs instead of whatever we configured —
confirmed this override happens regardless of what's passed to
`.UseAdMob()`, so the placeholder strings are inert while this flag is
`true`. Flipping it to `false` (once the AdMob account has real ad
units) plus swapping those two placeholder strings for real ones is
the entire "go live" step for ad identifiers.

### Why `DonateViewModel` can't talk to `Plugin.AdMob` directly

`Plugin.AdMob` only targets `net10.0-android;net10.0-ios;net10.0-maccatalyst`
(confirmed from its own `.csproj`) — there's no plain `net10.0` target.
`PiccoloReader.Core` is a plain `net10.0` class library (no MAUI platform
TFMs), so it flat-out cannot reference `Plugin.AdMob`'s types; NuGet
would refuse the restore. This app already has exactly this situation
solved elsewhere (`IPdfPageRenderer`/`PdfPageRenderer`,
`ILanguagePreferenceService`/`MauiLanguagePreferenceService`): a small
interface lives in Core with no platform-SDK dependency, and the App
project (which *does* target `net10.0-android`) provides the real
implementation, registered via DI. This feature follows the same split.

### `IDonateAdService` (Core) / `MauiDonateAdService` (App project)

```csharp
// PiccoloReader.Core/Services/IDonateAdService.cs
namespace PiccoloReader.Core.Services;

public interface IDonateAdService
{
    Task<bool> ShowRewardedAdAsync();
}
```

Returns `true` if the ad loaded and was shown, `false` if it failed to
load. `MauiDonateAdService` (App project, `PiccoloReader.Services`
namespace, same location pattern as `MauiAppStorageProvider`) adapts
this to the plugin's real, verified interface
(`Plugin.AdMob.Services.IRewardedAdService.CreateAd()` returning an
`Plugin.AdMob.IRewardedAd`, whose `OnAdLoaded`/`OnAdFailedToLoad`
events and `Load()`/`Show()` methods this wraps into a `Task<bool>`):

```csharp
// PiccoloReader/Services/MauiDonateAdService.cs
using Plugin.AdMob;
using Plugin.AdMob.Services;

namespace PiccoloReader.Services;

public class MauiDonateAdService : PiccoloReader.Core.Services.IDonateAdService
{
    private readonly IRewardedAdService _rewardedAdService;

    public MauiDonateAdService(IRewardedAdService rewardedAdService)
    {
        _rewardedAdService = rewardedAdService;
    }

    public Task<bool> ShowRewardedAdAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        var ad = _rewardedAdService.CreateAd();

        void Unsubscribe()
        {
            ad.OnAdLoaded -= OnLoaded;
            ad.OnAdFailedToLoad -= OnFailedToLoad;
        }

        void OnLoaded(object? sender, EventArgs e)
        {
            Unsubscribe();
            ad.Show();
            tcs.TrySetResult(true);
        }

        void OnFailedToLoad(object? sender, IAdError e)
        {
            Unsubscribe();
            tcs.TrySetResult(false);
        }

        ad.OnAdLoaded += OnLoaded;
        ad.OnAdFailedToLoad += OnFailedToLoad;
        ad.Load();

        return tcs.Task;
    }
}
```

`.UseAdMob()` (called in `MauiProgram.cs`) already registers
`Plugin.AdMob.Services.IRewardedAdService` internally, so
`MauiDonateAdService`'s constructor dependency resolves automatically —
we only need to register `IDonateAdService`/`MauiDonateAdService`
ourselves.

### `DonateViewModel` (Core) + `DonatePage` (View)

Same MVVM/DI shape as `SettingsViewModel`/`SettingsPage` — constructor
injection, `AddTransient` registration in `MauiProgram.cs`.

```csharp
public partial class DonateViewModel : ObservableObject
{
    private readonly IDonateAdService _donateAdService;

    [ObservableProperty]
    private bool _isAdLoading;

    public DonateViewModel(IDonateAdService donateAdService)
    {
        _donateAdService = donateAdService;
    }

    [RelayCommand]
    private async Task SeeAdAsync()
    {
        IsAdLoading = true;
        await _donateAdService.ShowRewardedAdAsync();
        IsAdLoading = false;
    }
}
```

`IsAdLoading` disables the "See ad" button while the ad is
loading/showing, so a slow network connection can't be mistaken for a
broken button via a double-tap. Whether `ShowRewardedAdAsync()` returns
`true` or `false`, `IsAdLoading` resets to `false` either way — a
failed load just makes the button tappable again with no error
message; the user can simply try again. There is no reward grant on ad
completion — no code path needs to run when the user finishes
watching; the ad impression itself is the entire "tip" mechanism. This
also makes `DonateViewModel` trivially testable: a fake
`IDonateAdService` is a one-line `Task<bool>` return, no AdMob event
plumbing needed in tests at all.

`DonatePage.xaml` contains: the localized explanation text, the "See
ad" button bound to `SeeAdCommand`/`IsAdLoading`, and the plugin's
`<admob:BannerAd />` control (no `AdUnitId` set — falls back to the
default configured in `.UseAdMob()`) pinned to the bottom of the
page's layout. The banner is scoped entirely to this page's XAML — no
shared layout/shell chrome changes, so no other page can be affected
by it.

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

- `DonateViewModel`'s `SeeAdCommand` (loading state true → false on
  both success and failure) gets unit tests in
  `PiccoloReader.Core.Tests`, against a fake `IDonateAdService` — no
  AdMob types or events involved at all, consistent with every other
  ViewModel test in this project.
- `MauiDonateAdService` itself (the real adapter wrapping
  `Plugin.AdMob`) is UI/platform layer, like `PdfPageRenderer` and
  `MauiAppStorageProvider` — verified manually on-device, not unit
  tested, matching how those are handled today.
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
