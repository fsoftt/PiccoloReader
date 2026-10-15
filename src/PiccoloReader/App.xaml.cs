using Microsoft.Extensions.DependencyInjection;
using PiccoloReader.Core.Services.Metronome;
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
		return window;
	}

	// The metronome only plays while the app is in the foreground.
	private static void StopMetronome()
	{
		var services = IPlatformApplication.Current?.Services;
		services?.GetService<IMetronomePlayer>()?.Stop();
		services?.GetService<MetronomeViewModel>()?.PersistIfDirty();
	}
}
