using Android.App;
using Android.Content.PM;
using Android.OS;

namespace PiccoloReader;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
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
    }
}
