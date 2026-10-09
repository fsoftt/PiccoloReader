using PiccoloReader.Core.Services;

namespace PiccoloReader.Views;

public partial class SplashPage : ContentPage
{
    private bool _navigated;

    public SplashPage()
    {
        InitializeComponent();

        LottieView.AnimationFailed += (_, _) => NavigateToApp();

        // Safety net in case the animation never raises AnimationCompleted or
        // AnimationFailed. The animation itself is 4s, but there's startup
        // overhead (page load, handler creation) before playback begins, so
        // this needs real margin above 4s to avoid cutting the animation off.
        Dispatcher.StartTimer(TimeSpan.FromSeconds(8), () =>
        {
            NavigateToApp();
            return false;
        });
    }

    private void OnAnimationCompleted(object? sender, EventArgs e) => NavigateToApp();

    private void NavigateToApp()
    {
        if (_navigated)
        {
            return;
        }

        _navigated = true;

        if (Application.Current is not null)
        {
            var services = IPlatformApplication.Current?.Services;
            var preferences = services?.GetService<ITutorialPreferenceService>();
            var tutorialPage = services?.GetService<TutorialPage>();

            if (preferences is not null && tutorialPage is not null && !preferences.GetTutorialCompleted())
            {
                var window = Window ?? Application.Current.Windows.FirstOrDefault();
                if (window is null)
                {
                    return;
                }

                tutorialPage.ViewModel.Finished += (_, _) => window.Page = new AppShell();
                window.Page = tutorialPage;
                return;
            }

            var target = Window ?? Application.Current.Windows.FirstOrDefault();
            if (target is not null)
            {
                target.Page = new AppShell();
            }
        }
    }
}
