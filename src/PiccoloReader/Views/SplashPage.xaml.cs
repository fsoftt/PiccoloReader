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
            Application.Current.Windows[0].Page = new AppShell();
        }
    }
}
