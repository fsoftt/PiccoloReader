using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using PiccoloReader.Core.Services.PageTurn;
using Microsoft.Extensions.DependencyInjection;

namespace PiccoloReader;

// "Open with" / Share for PDFs. No data scheme: matches both content:// and file://.
[IntentFilter(new[] { Android.Content.Intent.ActionView }, Categories = new[] { Android.Content.Intent.CategoryDefault, Android.Content.Intent.CategoryBrowsable }, DataMimeType = "application/pdf")]
[IntentFilter(new[] { Android.Content.Intent.ActionSend, Android.Content.Intent.ActionSendMultiple }, Categories = new[] { Android.Content.Intent.CategoryDefault }, DataMimeType = "application/pdf")]
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

        // A cold start from "open with" / share carries the PDF in the launch
        // intent. After a recreation the original intent is delivered again,
        // so only a fresh launch may consume it.
        if (savedInstanceState is null)
        {
            HandleSharedPdfIntent(Intent);
        }
    }

    protected override void OnNewIntent(Android.Content.Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleSharedPdfIntent(intent);
    }

    // Copies the shared PDFs while the temporary read grant is valid, then
    // hands them to the import flow (which asks for the destination). The
    // intent is replaced so it can never be handled twice.
    private void HandleSharedPdfIntent(Android.Content.Intent? intent)
    {
        if (!Platforms.Android.SharedPdfIntentReader.IsSharedPdfIntent(intent))
        {
            return;
        }

        var uris = Platforms.Android.SharedPdfIntentReader.GetUris(intent!);
        Intent = new Android.Content.Intent(Android.Content.Intent.ActionMain);

        var flow = IPlatformApplication.Current?.Services.GetService<PiccoloReader.Services.SharedPdfImportFlow>();
        if (flow is null || uris.Count == 0)
        {
            return;
        }

        var context = ApplicationContext ?? this;
        _ = Task.Run(async () =>
        {
            var (paths, unreadable) = await Platforms.Android.SharedPdfIntentReader.CopyToCacheAsync(context, uris);
            flow.Enqueue(paths, unreadable);
        });
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

    // Bluetooth page-turn pedals and keyboards. Handled here rather than in
    // a view so a focused child (list, scroll view) can't swallow the key.
    // A key the page took is swallowed through its repeats and its key-up, so
    // holding a pedal turns exactly one page and nothing else reacts to it.
    // The per-(device, key) bookkeeping lives in PedalKeyTracker. Android
    // eats the key-down of a navigation key that leaves touch mode (the
    // first Page/D-pad press after a screen touch) but still delivers its
    // key-up; that unseen key-up turns the page instead.
    private readonly PedalKeyTracker _pedalKeys = new();

    // A lost key-up (focus change, pause) must never leave a key stuck.
    private void ClearSwallowed() => _pedalKeys.Clear();

    protected override void OnPause()
    {
        ClearSwallowed();
        base.OnPause();
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        if (!hasFocus)
        {
            ClearSwallowed();
        }

        base.OnWindowFocusChanged(hasFocus);
    }

    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e is null || (e.Action != KeyEventActions.Down && e.Action != KeyEventActions.Up))
        {
            return base.DispatchKeyEvent(e);
        }

        try
        {
            var key = ToPageTurnKey(e.KeyCode);
            if (key == PageTurnKey.Other)
            {
                return base.DispatchKeyEvent(e);
            }

            var id = PedalKeyTracker.IdOf(e.DeviceId, (int)e.KeyCode);
            var isDown = e.Action == KeyEventActions.Down;
            var decision = isDown ? _pedalKeys.OnDown(id, e.RepeatCount) : _pedalKeys.OnUp(id);
            if (decision == PedalKeyDecision.Swallow)
            {
                return true;
            }

            if (decision == PedalKeyDecision.TryTurn)
            {
                // Every Down that does not turn (typing, focused button,
                // overlay, no viewer yet, an exception) is recorded as passed
                // through, so its repeats and Up never look like a new press.
                var handled = false;
                try
                {
                    handled = CanTurnPagesWithKeys(key)
                        && IPlatformApplication.Current?.Services.GetService<IPageTurnInput>() is { } input
                        && input.Handle(key, e.IsShiftPressed);
                }
                catch (Exception ex)
                {
                    Android.Util.Log.Warn("PiccoloReader", $"Pedal page turn failed: {ex}");
                }

                if (isDown)
                {
                    _pedalKeys.Record(id, handled);
                }

                if (handled)
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("PiccoloReader", $"Pedal page turn failed: {ex}");
        }

        return base.DispatchKeyEvent(e);
    }

    // Not while typing, not over a modal page (dialogs have their own window
    // and never reach the activity), and - for the keys that also drive
    // focus navigation (Enter, D-pad) - not while a button or similar
    // actionable view holds focus, so a hardware keyboard, D-pad or
    // TalkBack can still operate it.
    private bool CanTurnPagesWithKeys(PageTurnKey key)
    {
        var focus = CurrentFocus;
        if (focus is Android.Widget.EditText)
        {
            return false;
        }

        if (PedalKeyMap.IsFocusNavigationKey(key) && focus is { Clickable: true, IsFocused: true }
            && focus is not Microsoft.Maui.Platform.ContentViewGroup
            && focus is not AndroidX.RecyclerView.Widget.RecyclerView)
        {
            return false;
        }

        return Microsoft.Maui.Controls.Shell.Current?.Navigation.ModalStack.Count is null or 0;
    }

    private static PageTurnKey ToPageTurnKey(Keycode code) => code switch
    {
        Keycode.PageDown => PageTurnKey.PageDown,
        Keycode.PageUp => PageTurnKey.PageUp,
        Keycode.DpadRight => PageTurnKey.DpadRight,
        Keycode.DpadLeft => PageTurnKey.DpadLeft,
        Keycode.DpadDown => PageTurnKey.DpadDown,
        Keycode.DpadUp => PageTurnKey.DpadUp,
        Keycode.Space => PageTurnKey.Space,
        Keycode.Enter => PageTurnKey.Enter,
        Keycode.NumpadEnter => PageTurnKey.NumpadEnter,
        Keycode.MediaNext => PageTurnKey.MediaNext,
        Keycode.MediaPrevious => PageTurnKey.MediaPrevious,
        _ => PageTurnKey.Other
    };

    // Result of the library folder picker (reinstall recovery).
    protected override void OnActivityResult(int requestCode, Result resultCode, Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        PiccoloReader.Platforms.Android.ExternalLibrary.AndroidExternalLibraryStore.OnPickResult(requestCode, resultCode == Result.Ok, data);
    }

    public override void OnConfigurationChanged(Android.Content.Res.Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        ClearSwallowed();
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
