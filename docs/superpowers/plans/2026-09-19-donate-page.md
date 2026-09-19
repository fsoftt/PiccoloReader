# Donate Page (Ads via AdMob) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Donate" flyout menu item opening a page with localized explanation text, a "See ad" button that shows a rewarded ad, and a banner ad fixed at the bottom of that page only.

**Architecture:** `Plugin.AdMob` (MIT, net10.0-android/ios/maccatalyst) provides the ad SDK wrapper. Since `PiccoloReader.Core` is a plain `net10.0` library that can't reference `Plugin.AdMob` (no plain `net10.0` target on that package), a small `IDonateAdService` interface lives in Core with a `MauiDonateAdService` adapter in the App project — the same split already used for `IPdfPageRenderer`/`PdfPageRenderer`.

**Tech Stack:** .NET MAUI / CommunityToolkit.Mvvm, `Plugin.AdMob` 10.0.90, xUnit.

**Spec:** [`docs/superpowers/specs/2026-09-19-donate-page-design.md`](../specs/2026-09-19-donate-page-design.md)

## Global Constraints

- Android only for now — the Donate flyout item is hidden on iOS.
- No AdMob account/ad units exist yet — `AdConfig.UseTestAdUnitIds = true` makes the plugin use Google's official test ad unit IDs regardless of what's configured; the AndroidManifest App ID uses Google's official public test App ID (`ca-app-pub-3940256099942544~3347511713`).
- No in-app reward system exists — the rewarded ad's completion has no reward-granting code path; showing the ad is the entire "tip."

---

### Task 1: Wire up `Plugin.AdMob` (package, MauiProgram, manifest)

**Files:**
- Modify: `src/PiccoloReader/PiccoloReader.csproj`
- Modify: `src/PiccoloReader/MauiProgram.cs`
- Modify: `src/PiccoloReader/Platforms/Android/AndroidManifest.xml`

**Interfaces:**
- Produces: `.UseAdMob(...)` called with placeholder default ad unit IDs, `AdConfig.UseTestAdUnitIds = true` set — both consumed implicitly by later tasks (`MauiDonateAdService.CreateAd()` and `DonatePage`'s `<admob:BannerAd>` both rely on the defaults configured here).

- [ ] **Step 1: Add the package reference, and two required version bumps**

In `src/PiccoloReader/PiccoloReader.csproj`, add to the existing `PackageReference` `ItemGroup` (after `Microsoft.Maui.Controls.Compatibility`):

```xml
		<PackageReference Include="Plugin.AdMob" Version="10.0.90" />
```

This alone isn't enough - restore fails with a downgrade conflict,
since `Plugin.AdMob` 10.0.90 requires `Microsoft.Maui.Controls`/
`.Compatibility` >= 10.0.90, but this project pins `Microsoft.Maui.Controls`
to `$(MauiVersion)` (resolves to `10.0.20` on this SDK) and
`.Compatibility` explicitly to `10.0.20`. Change both:

```xml
		<PackageReference Include="Microsoft.Maui.Controls" Version="10.0.90" />
		<PackageReference Include="Microsoft.Maui.Controls.Compatibility" Version="10.0.90" />
```

Also bump the Android `SupportedOSPlatformVersion` from `21.0` to
`23.0` (further up in the same file) - the Google Mobile Ads SDK's
transitive `androidx.lifecycle` dependency requires minSdk 23, and the
Android manifest merger fails outright below that:

```xml
		<!-- 23 (Android 6.0), not 21 - the Google Mobile Ads SDK's transitive
		     androidx.lifecycle dependency (pulled in via Plugin.AdMob) requires
		     minSdk 23. Android 21-22 devices are vanishingly rare at this point. -->
		<SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'">23.0</SupportedOSPlatformVersion>
```

- [ ] **Step 2: Wire `.UseAdMob()` into the builder chain**

In `src/PiccoloReader/MauiProgram.cs`, add a using at the top:

```csharp
using Plugin.AdMob;
using Plugin.AdMob.Configuration;
```

Change the builder chain from:

```csharp
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.UseSkiaSharp()
			.ConfigureFonts(fonts =>
```

to:

```csharp
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.UseSkiaSharp()
			// Placeholder ad unit IDs - inert while AdConfig.UseTestAdUnitIds is
			// true (see below). Swap these for real ones from the AdMob console
			// once the account/ad units exist, alongside flipping that flag.
			.UseAdMob(
				androidDefaultBannerAdUnitId: "ca-app-pub-8091715200642863/1000000001",
				androidDefaultRewardedAdUnitId: "ca-app-pub-8091715200642863/1000000002")
			.ConfigureFonts(fonts =>
```

Immediately after the existing `Batteries_V2.Init();` line, add:

```csharp
		// No AdMob account/ad units exist yet - this makes the plugin
		// substitute Google's official test ad unit IDs regardless of what
		// was passed to .UseAdMob() above. Flip to false once real ad units
		// exist (see docs/play-store-release.md for the equivalent Play
		// Store "test now, real config later" pattern).
		AdConfig.UseTestAdUnitIds = true;
```

- [ ] **Step 3: Add the AdMob App ID and required permissions to the manifest**

In `src/PiccoloReader/Platforms/Android/AndroidManifest.xml`, replace:

```xml
	<application android:allowBackup="true" android:icon="@mipmap/appicon" android:roundIcon="@mipmap/appicon_round" android:supportsRtl="true"></application>
</manifest>
```

with:

```xml
	<application android:allowBackup="true" android:icon="@mipmap/appicon" android:roundIcon="@mipmap/appicon_round" android:supportsRtl="true">
		<!-- Google's official public test App ID - swap for the real one
		     once an AdMob app entry exists for PiccoloReader. -->
		<meta-data android:name="com.google.android.gms.ads.APPLICATION_ID" android:value="ca-app-pub-3940256099942544~3347511713" />
	</application>
	<!-- Required by the Google Mobile Ads SDK to load ads (Donate page only). -->
	<uses-permission android:name="android.permission.INTERNET" />
	<uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />
</manifest>
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android`
Expected: 0 errors. (If package restore fails, double check the `Plugin.AdMob` version `10.0.90` is still current on nuget.org - use whatever the latest published version is if not.)

- [ ] **Step 5: Run the full Core test suite**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
Expected: PASS, no regressions (this task doesn't touch Core).

- [ ] **Step 6: Commit**

```bash
git add src/PiccoloReader/PiccoloReader.csproj src/PiccoloReader/MauiProgram.cs src/PiccoloReader/Platforms/Android/AndroidManifest.xml
git commit -m "feat: wire up Plugin.AdMob (package, builder registration, manifest)"
```

---

### Task 2: Localization strings

**Files:**
- Modify: `src/PiccoloReader.Core/Resources/Strings/AppStrings.resx`
- Modify: `src/PiccoloReader.Core/Resources/Strings/AppStrings.es.resx`
- Modify: `src/PiccoloReader.Core/Resources/Strings/AppStrings.cs`
- Modify: `tests/PiccoloReader.Core.Tests/Resources/AppStringsTests.cs`

**Interfaces:**
- Produces: `AppStrings.DonateMenuItem`, `AppStrings.DonatePageTitle`, `AppStrings.DonateExplanation`, `AppStrings.SeeAdButton` (all `static string`) — consumed by Task 4's `AppShell.xaml`/`DonatePage.xaml`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/PiccoloReader.Core.Tests/Resources/AppStringsTests.cs`:

```csharp
[Fact]
public void DonateMenuItem_SpanishCulture_ReturnsSpanishValue()
{
    var original = CultureInfo.CurrentUICulture;
    try
    {
        CultureInfo.CurrentUICulture = new CultureInfo("es");
        Assert.Equal("Donar", AppStrings.DonateMenuItem);
    }
    finally
    {
        CultureInfo.CurrentUICulture = original;
    }
}

[Fact]
public void SeeAdButton_EnglishCulture_ReturnsEnglishValue()
{
    var original = CultureInfo.CurrentUICulture;
    try
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        Assert.Equal("See ad", AppStrings.SeeAdButton);
    }
    finally
    {
        CultureInfo.CurrentUICulture = original;
    }
}

[Fact]
public void DonateExplanation_SpanishCulture_ContainsSpanishText()
{
    var original = CultureInfo.CurrentUICulture;
    try
    {
        CultureInfo.CurrentUICulture = new CultureInfo("es");
        Assert.Contains("propina", AppStrings.DonateExplanation);
    }
    finally
    {
        CultureInfo.CurrentUICulture = original;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter "DonateMenuItem|SeeAdButton|DonateExplanation"`
Expected: FAIL to compile — `AppStrings.DonateMenuItem`/`SeeAdButton`/`DonateExplanation` don't exist yet.

- [ ] **Step 3: Add the English resx entries**

In `src/PiccoloReader.Core/Resources/Strings/AppStrings.resx`, add before the closing `</root>`:

```xml
  <data name="DonateMenuItem" xml:space="preserve"><value>Donate</value></data>
  <data name="DonatePageTitle" xml:space="preserve"><value>Donate</value></data>
  <data name="DonateExplanation" xml:space="preserve"><value>I don't like subscriptions. I built this app to be free because that's the right choice.

If you'd like to give me a tip, tap the button below — it'll show a short ad.</value></data>
  <data name="SeeAdButton" xml:space="preserve"><value>See ad</value></data>
</root>
```

- [ ] **Step 4: Add the Spanish resx entries**

In `src/PiccoloReader.Core/Resources/Strings/AppStrings.es.resx`, add before the closing `</root>`:

```xml
  <data name="DonateMenuItem" xml:space="preserve"><value>Donar</value></data>
  <data name="DonatePageTitle" xml:space="preserve"><value>Donar</value></data>
  <data name="DonateExplanation" xml:space="preserve"><value>No me gustan las suscripciones. Hice esta app gratis porque es lo correcto.

Si quieres darme una propina, toca el botón de abajo — se mostrará un anuncio corto.</value></data>
  <data name="SeeAdButton" xml:space="preserve"><value>Ver anuncio</value></data>
</root>
```

- [ ] **Step 5: Add the AppStrings.cs properties**

In `src/PiccoloReader.Core/Resources/Strings/AppStrings.cs`, add near the other `Settings`/`LanguageSectionHeader` properties:

```csharp
    public static string DonateMenuItem => Get(nameof(DonateMenuItem));
    public static string DonatePageTitle => Get(nameof(DonatePageTitle));
    public static string DonateExplanation => Get(nameof(DonateExplanation));
    public static string SeeAdButton => Get(nameof(SeeAdButton));
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter "DonateMenuItem|SeeAdButton|DonateExplanation"`
Expected: PASS

- [ ] **Step 7: Run the full Core test suite**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
Expected: PASS, no regressions.

- [ ] **Step 8: Commit**

```bash
git add src/PiccoloReader.Core/Resources/Strings/AppStrings.resx src/PiccoloReader.Core/Resources/Strings/AppStrings.es.resx src/PiccoloReader.Core/Resources/Strings/AppStrings.cs tests/PiccoloReader.Core.Tests/Resources/AppStringsTests.cs
git commit -m "feat: add Donate page localization strings (English + Spanish)"
```

---

### Task 3: `IDonateAdService` + `DonateViewModel`

**Files:**
- Create: `src/PiccoloReader.Core/Services/IDonateAdService.cs`
- Create: `src/PiccoloReader.Core/ViewModels/DonateViewModel.cs`
- Create: `tests/PiccoloReader.Core.Tests/FakeDonateAdService.cs`
- Test: `tests/PiccoloReader.Core.Tests/ViewModels/DonateViewModelTests.cs`

**Interfaces:**
- Produces: `IDonateAdService.ShowRewardedAdAsync() : Task<bool>` — consumed by Task 4's `MauiDonateAdService` (the real implementation) and DI registration.
- Produces: `DonateViewModel(IDonateAdService)`, with `IsAdLoading` (`bool`, `[ObservableProperty]`) and `SeeAdCommand` (`IAsyncRelayCommand`, from `[RelayCommand] SeeAdAsync()`) — consumed by Task 4's `DonatePage.xaml` bindings and DI registration.

- [ ] **Step 1: Write the interface**

Create `src/PiccoloReader.Core/Services/IDonateAdService.cs`:

```csharp
namespace PiccoloReader.Core.Services;

public interface IDonateAdService
{
    Task<bool> ShowRewardedAdAsync();
}
```

- [ ] **Step 2: Write the fake test double**

Create `tests/PiccoloReader.Core.Tests/FakeDonateAdService.cs`:

```csharp
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests;

public class FakeDonateAdService : IDonateAdService
{
    public bool ResultToReturn { get; set; } = true;

    public int ShowRewardedAdCallCount { get; private set; }

    public Task<bool> ShowRewardedAdAsync()
    {
        ShowRewardedAdCallCount++;
        return Task.FromResult(ResultToReturn);
    }
}
```

- [ ] **Step 3: Write the failing tests**

Create `tests/PiccoloReader.Core.Tests/ViewModels/DonateViewModelTests.cs`:

```csharp
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Core.Tests.ViewModels;

public class DonateViewModelTests
{
    [Fact]
    public async Task SeeAdCommand_Success_CallsShowRewardedAdAndResetsLoading()
    {
        var adService = new FakeDonateAdService { ResultToReturn = true };
        var sut = new DonateViewModel(adService);

        await sut.SeeAdCommand.ExecuteAsync(null);

        Assert.Equal(1, adService.ShowRewardedAdCallCount);
        Assert.False(sut.IsAdLoading);
    }

    [Fact]
    public async Task SeeAdCommand_Failure_StillResetsLoading()
    {
        var adService = new FakeDonateAdService { ResultToReturn = false };
        var sut = new DonateViewModel(adService);

        await sut.SeeAdCommand.ExecuteAsync(null);

        Assert.Equal(1, adService.ShowRewardedAdCallCount);
        Assert.False(sut.IsAdLoading);
    }

    [Fact]
    public void IsAdLoading_InitiallyFalse()
    {
        var sut = new DonateViewModel(new FakeDonateAdService());

        Assert.False(sut.IsAdLoading);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter DonateViewModelTests`
Expected: FAIL to compile — `DonateViewModel` doesn't exist yet.

- [ ] **Step 5: Implement `DonateViewModel`**

Create `src/PiccoloReader.Core/ViewModels/DonateViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.ViewModels;

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

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj --filter DonateViewModelTests`
Expected: PASS

- [ ] **Step 7: Run the full Core test suite**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
Expected: PASS, no regressions.

- [ ] **Step 8: Commit**

```bash
git add src/PiccoloReader.Core/Services/IDonateAdService.cs src/PiccoloReader.Core/ViewModels/DonateViewModel.cs tests/PiccoloReader.Core.Tests/FakeDonateAdService.cs tests/PiccoloReader.Core.Tests/ViewModels/DonateViewModelTests.cs
git commit -m "feat: add IDonateAdService and DonateViewModel"
```

---

### Task 4: `MauiDonateAdService`, `DonatePage`, flyout entry

**Files:**
- Create: `src/PiccoloReader/Services/MauiDonateAdService.cs`
- Create: `src/PiccoloReader/PlatformInfo.cs`
- Create: `src/PiccoloReader/Converters/InvertedBoolConverter.cs`
- Create: `src/PiccoloReader/Views/DonatePage.xaml`
- Create: `src/PiccoloReader/Views/DonatePage.xaml.cs`
- Modify: `src/PiccoloReader/AppShell.xaml`
- Modify: `src/PiccoloReader/MauiProgram.cs`

**Interfaces:**
- Consumes: `IDonateAdService`, `DonateViewModel` (Task 3); `Plugin.AdMob.Services.IRewardedAdService` (registered by `.UseAdMob()` in Task 1); `AppStrings.DonateMenuItem`/`DonatePageTitle`/`DonateExplanation`/`SeeAdButton` (Task 2).

No automated tests for this task — MAUI page/XAML/Shell changes in this
project are verified manually on-device (see every prior UI task in
`docs/superpowers/plans/roadmap.md`).

- [ ] **Step 1: Implement `MauiDonateAdService`**

Create `src/PiccoloReader/Services/MauiDonateAdService.cs`:

```csharp
using Plugin.AdMob;
using Plugin.AdMob.Services;
using PiccoloReader.Core.Services;

namespace PiccoloReader.Services;

public class MauiDonateAdService : IDonateAdService
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

- [ ] **Step 2: Add the platform-visibility helper**

Create `src/PiccoloReader/PlatformInfo.cs`:

```csharp
namespace PiccoloReader;

public static class PlatformInfo
{
    public static bool IsAndroid => DeviceInfo.Platform == DevicePlatform.Android;
}
```

- [ ] **Step 3: Add the `InvertedBoolConverter`**

No inverted-bool converter exists yet (confirmed: `src/PiccoloReader/Converters/`
only has `ByteArrayToImageSourceConverter` and `StringNotEmptyConverter`).
Create `src/PiccoloReader/Converters/InvertedBoolConverter.cs`:

```csharp
using System.Globalization;

namespace PiccoloReader.Converters;

public class InvertedBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : value;
}
```

- [ ] **Step 4: Create `DonatePage.xaml`**

Create `src/PiccoloReader/Views/DonatePage.xaml`. Converters in this
project are declared per-page in `<ContentPage.Resources>`, keyed
locally (see `FolderPage.xaml`'s `StringNotEmptyConverter` for the
exact pattern being followed here) - not registered globally:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ContentPage
    x:Class="PiccoloReader.Views.DonatePage"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:admob="clr-namespace:Plugin.AdMob;assembly=Plugin.AdMob"
    xmlns:converters="clr-namespace:PiccoloReader.Converters"
    xmlns:strings="clr-namespace:PiccoloReader.Core.Resources.Strings;assembly=PiccoloReader.Core"
    Title="{x:Static strings:AppStrings.DonatePageTitle}">

    <ContentPage.Resources>
        <converters:InvertedBoolConverter x:Key="InvertedBool" />
    </ContentPage.Resources>

    <Grid RowDefinitions="*,Auto">

        <VerticalStackLayout Grid.Row="0" Padding="16" Spacing="16" VerticalOptions="Center">
            <Label Text="{x:Static strings:AppStrings.DonateExplanation}" FontSize="16" />
            <Button
                Text="{x:Static strings:AppStrings.SeeAdButton}"
                Command="{Binding SeeAdCommand}"
                IsEnabled="{Binding IsAdLoading, Converter={StaticResource InvertedBool}}" />
        </VerticalStackLayout>

        <admob:BannerAd Grid.Row="1" />

    </Grid>
</ContentPage>
```

- [ ] **Step 5: Create `DonatePage.xaml.cs`**

Create `src/PiccoloReader/Views/DonatePage.xaml.cs`:

```csharp
using PiccoloReader.Core.ViewModels;

namespace PiccoloReader.Views;

public partial class DonatePage : ContentPage
{
    public DonatePage(DonateViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
```

- [ ] **Step 6: Add the flyout entry**

In `src/PiccoloReader/AppShell.xaml`, add between the Library and
Settings `FlyoutItem`s:

```xml
    <FlyoutItem
        Title="{x:Static strings:AppStrings.DonateMenuItem}"
        Route="donate"
        IsVisible="{x:Static local:PlatformInfo.IsAndroid}">
        <FlyoutItem.FlyoutIcon>
            <FontImageSource
                Glyph="&#xE87E;"
                FontFamily="MaterialOutlined"
                Size="24"
                Color="{AppThemeBinding Light={StaticResource Primary}, Dark={StaticResource PrimaryDark}}" />
        </FlyoutItem.FlyoutIcon>
        <ShellContent ContentTemplate="{DataTemplate views:DonatePage}" />
    </FlyoutItem>
```

Add the `local` namespace to the `<Shell>` root element's attributes
(alongside the existing `xmlns:views`/`xmlns:strings`):

```xml
    xmlns:local="clr-namespace:PiccoloReader"
```

- [ ] **Step 7: Register the new services/page in `MauiProgram.cs`**

Add near the other `IAppStorageProvider`/`ILanguagePreferenceService` registrations:

```csharp
		builder.Services.AddSingleton<IDonateAdService, MauiDonateAdService>();
```

Add near the other `AddTransient<SettingsViewModel>()`/`AddTransient<SettingsPage>()` lines:

```csharp
		builder.Services.AddTransient<DonateViewModel>();
```

```csharp
		builder.Services.AddTransient<DonatePage>();
```

- [ ] **Step 8: Build to verify**

Run: `dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android`
Expected: 0 errors.

- [ ] **Step 9: Run the full Core test suite**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
Expected: PASS, no regressions.

- [ ] **Step 10: Commit**

```bash
git add src/PiccoloReader/Services/MauiDonateAdService.cs src/PiccoloReader/PlatformInfo.cs src/PiccoloReader/Converters/InvertedBoolConverter.cs src/PiccoloReader/Views/DonatePage.xaml src/PiccoloReader/Views/DonatePage.xaml.cs src/PiccoloReader/AppShell.xaml src/PiccoloReader/MauiProgram.cs
git commit -m "feat: add DonatePage, flyout entry, and MauiDonateAdService"
```

---

### Task 5: Manual end-to-end verification, regression, PR

**Files:** none (verification only)

- [ ] **Step 1: Build and install a Release APK**

Follow the project's established Android testing workflow (see
[[project-android-env]]): `dotnet build src/PiccoloReader/PiccoloReader.csproj -f net10.0-android -c Release -p:RuntimeIdentifier=android-arm64`, then `adb install -r` the resulting `*-Signed.apk` on the emulator or a connected device.

- [ ] **Step 2: Verify the flyout entry and page in English**

Open the flyout menu, confirm "Donate" appears (with the heart-outline
icon) between Library and Settings. Tap it, confirm the explanation
text and "See ad" button render, and confirm the banner ad loads at
the bottom of the page (Google's test banner renders a visible "Test
Ad" placeholder — if nothing appears, check logcat for AdMob errors
before assuming it's broken, since ad loads depend on the emulator/
device actually having network access).

- [ ] **Step 3: Verify the rewarded ad flow**

Tap "See ad". Confirm the button disables while loading, then a
full-screen test rewarded ad appears. Close it, confirm the button is
tappable again and can be tapped a second time.

- [ ] **Step 4: Verify no ads appear anywhere else**

Navigate to Library, a Folder, the Sheet Viewer, and Settings. Confirm
none of them show any banner or ad content.

- [ ] **Step 5: Verify Spanish localization**

Switch language to Spanish via Settings (restart when prompted),
reopen the flyout, confirm "Donar" appears, open the page, confirm the
explanation text and "Ver anuncio" button are in Spanish.

- [ ] **Step 6: Verify iOS doesn't show the menu item**

This can't be verified on-device (no iOS build/simulator in this
project's toolchain per [[project-android-env]]) - confirmed instead
by code review of `PlatformInfo.IsAndroid`'s binding in Task 4.

- [ ] **Step 7: Run the full regression suite one more time**

Run: `dotnet test tests/PiccoloReader.Core.Tests/PiccoloReader.Core.Tests.csproj`
Expected: PASS, no regressions.

- [ ] **Step 8: Push and open the PR**

```bash
git push -u origin feature/donate-page
gh pr create --title "Add Donate page with rewarded + banner ads" --body "$(cat <<'EOF'
## Summary
- Adds a "Donate" flyout menu item (Android only) opening a page explaining the developer's no-subscriptions philosophy, with a "See ad" rewarded-ad button and a banner ad fixed at the bottom of that page only - no ads anywhere else in the app.
- Uses Plugin.AdMob (MIT, free GDPR/UMP consent included). Since it doesn't publish a plain net10.0 target, PiccoloReader.Core can't reference it directly - IDonateAdService (Core) / MauiDonateAdService (App project) follows the same split already used for IPdfPageRenderer.
- No AdMob account/ad units exist yet - wired up against Google's official test App ID and test ad unit IDs (AdConfig.UseTestAdUnitIds = true), with a documented one-flag-plus-two-strings swap to production values once the account exists.

## Test plan
- [x] Unit tests for DonateViewModel (success/failure both reset loading state) and the new AppStrings entries
- [x] Manual on-device verification: flyout entry + icon, banner ad renders, rewarded ad flow (load → show → dismiss → re-tappable), no ads on any other page, English and Spanish text
EOF
)"
```

Send the PR-ready push notification per established project preference.
