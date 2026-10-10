using Android.App;
using Android.Content.PM;
using Android.OS;

namespace PiccoloReader;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density | ConfigChanges.FontScale)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Android's automatic "force dark" independently re-darkens/re-lightens
        // colors on top of our own AppThemeBinding resolution, producing
        // inconsistent results (e.g. a card explicitly styled as near-black in
        // dark mode rendering light gray instead) - found by actually toggling
        // system dark mode and comparing against what the XAML specified.
        if (OperatingSystem.IsAndroidVersionAtLeast(29) && Window?.DecorView is { } decorView)
        {
            decorView.ForceDarkAllowed = false;
        }

        UpdateSystemBarAppearance();
        DisableFocusHighlight();
    }

    // After the soft keyboard closes with the Back key the window is in
    // keyboard (non-touch) mode and Android paints its default focus
    // highlight (a grey scrim) over whichever large container grabbed
    // focus - the whole page, status bar included - until the next focus
    // change (e.g. a tab switch). Turn the highlight off for every view
    // that receives focus.
    private void DisableFocusHighlight()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26) || Window?.DecorView is not { } decor)
        {
            return;
        }

        decor.DefaultFocusHighlightEnabled = false;
        if (decor.ViewTreeObserver is { } observer)
        {
            observer.GlobalFocusChange += (_, e) =>
            {
                if (e.NewFocus is { } focused)
                {
                    focused.DefaultFocusHighlightEnabled = false;
                }
            };
        }
    }

    // Result of the library folder picker (reinstall recovery).
    protected override void OnActivityResult(int requestCode, Result resultCode, Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        PiccoloReader.Platforms.Android.ExternalLibrary.AndroidExternalLibraryStore.OnPickResult(requestCode, resultCode == Result.Ok, data);
    }

    public override void OnConfigurationChanged(Android.Content.Res.Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        UpdateSystemBarAppearance();
    }

    // The Shell app bar (page Background token) draws behind the status bar
    // edge-to-edge, so only the icon contrast needs to follow the theme:
    // dark icons on the light background, light icons on the dark one.
    private void UpdateSystemBarAppearance()
    {
        if (Window?.DecorView is not { } decor)
        {
            return;
        }

        var isDark = (Resources?.Configuration?.UiMode & Android.Content.Res.UiMode.NightMask) == Android.Content.Res.UiMode.NightYes;
        // Match the page Background token (BackgroundLight / BackgroundDark).
        var barColor = Android.Graphics.Color.ParseColor(isDark ? "#0E0E16" : "#F6F6FA");
#pragma warning disable CA1422
        Window.SetStatusBarColor(barColor);
        Window.SetNavigationBarColor(barColor);
#pragma warning restore CA1422
        var controller = AndroidX.Core.View.WindowCompat.GetInsetsController(Window, decor);
        if (controller is not null)
        {
            controller.AppearanceLightStatusBars = !isDark;
            controller.AppearanceLightNavigationBars = !isDark;
        }
    }
}
