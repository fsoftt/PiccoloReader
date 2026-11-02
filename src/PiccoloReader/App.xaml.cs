using Microsoft.Extensions.DependencyInjection;
using PiccoloReader.Core.Services.Metronome;
using PiccoloReader.Core.Services.Piano;
using PiccoloReader.Core.ViewModels;
using PiccoloReader.Views;

namespace PiccoloReader;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new SplashPage());
		window.Stopped += (_, _) => StopMetronome();
		window.Resumed += (_, _) => IPlatformApplication.Current?.Services.GetService<IPianoService>()?.Resume();
		return window;
	}

	// The metronome and the piano only sound while the app is in the foreground.
	private static void StopMetronome()
	{
		var services = IPlatformApplication.Current?.Services;
		services?.GetService<IMetronomePlayer>()?.Stop();
		services?.GetService<MetronomeViewModel>()?.PersistIfDirty();
		services?.GetService<TunerViewModel>()?.PersistIfDirty();
		services?.GetService<IPianoService>()?.Suspend();
		services?.GetService<PianoViewModel>()?.PersistIfDirty();
	}
}
