using PiccoloReader.Views;

namespace PiccoloReader;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute("folder", typeof(FolderPage));
		Routing.RegisterRoute("sheetviewer", typeof(SheetViewerPage));
	}

	private async void OnAboutLinkTapped(object? sender, TappedEventArgs e)
	{
		await Launcher.Default.OpenAsync(new Uri("https://fsoftt.github.io"));
	}
}
