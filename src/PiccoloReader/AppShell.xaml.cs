using PiccoloReader.Views;

namespace PiccoloReader;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute("folder", typeof(FolderPage));
	}
}
