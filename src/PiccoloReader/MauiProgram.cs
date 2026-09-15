using CommunityToolkit.Maui;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;
using UraniumUI;
using PiccoloReader.Core.Data;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.ViewModels;
using PiccoloReader.Services;
using PiccoloReader.Views;
using SQLitePCL;

namespace PiccoloReader;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.UseSkiaSharp()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
				fonts.AddFont("Pacifico-Regular.ttf", "Pacifico");
				fonts.AddMaterialSymbolsFonts();
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		Batteries_V2.Init();

		builder.Services.AddSingleton<IAppStorageProvider, MauiAppStorageProvider>();
		builder.Services.AddSingleton(sp =>
		{
			var storage = sp.GetRequiredService<IAppStorageProvider>();
			var database = new AppDatabase(storage.DatabasePath);

			// Task.Run moves InitializeAsync's continuations onto a thread
			// pool thread with no captured SynchronizationContext. Awaiting
			// it directly here would deadlock: the UI thread's
			// SynchronizationContext is what InitializeAsync's internal
			// awaits would try to resume on, but GetResult() is already
			// blocking that same thread waiting for it to finish.
			Task.Run(() => database.InitializeAsync()).GetAwaiter().GetResult();

			return database;
		});
		builder.Services.AddSingleton<LibraryService>();
		builder.Services.AddSingleton<PdfImportService>();

		builder.Services.AddTransient<LibraryViewModel>();
		builder.Services.AddTransient<FolderViewModel>();

		builder.Services.AddTransient<LibraryPage>();
		builder.Services.AddTransient<FolderPage>();

		return builder.Build();
	}
}
