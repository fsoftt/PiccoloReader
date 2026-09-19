using System.Globalization;
using CommunityToolkit.Maui;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Plugin.AdMob;
using Plugin.AdMob.Configuration;
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
			// Placeholder ad unit IDs - inert while AdConfig.UseTestAdUnitIds is
			// true (see below). Swap these for real ones from the AdMob console
			// once the account/ad units exist, alongside flipping that flag.
			.UseAdMob(
				androidDefaultBannerAdUnitId: "ca-app-pub-8091715200642863/1000000001",
				androidDefaultRewardedAdUnitId: "ca-app-pub-8091715200642863/1000000002")
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
				fonts.AddFont("Pacifico-Regular.ttf", "Pacifico");
				fonts.AddFont("Bravura.otf", "Bravura");
				fonts.AddMaterialSymbolsFonts();
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		Batteries_V2.Init();

		// No AdMob account/ad units exist yet - this makes the plugin
		// substitute Google's official test ad unit IDs regardless of what
		// was passed to .UseAdMob() above. Flip to false once real ad units
		// exist (see docs/play-store-release.md for the equivalent Play
		// Store "test now, real config later" pattern).
		AdConfig.UseTestAdUnitIds = true;

		var savedLanguageCode = Preferences.Default.Get("AppLanguage", (string?)null);
#if ANDROID
		// CultureInfo.CurrentUICulture does not sync with the Android device
		// locale on this runtime (it stays Invariant) - read the OS locale
		// directly via the Java API instead.
		var deviceLanguageCode = Java.Util.Locale.Default?.Language ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
#else
		var deviceLanguageCode = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
#endif
		var resolvedLanguageCode = LanguageResolver.ResolveLanguageCode(savedLanguageCode, deviceLanguageCode);

		if (savedLanguageCode is null)
		{
			Preferences.Default.Set("AppLanguage", resolvedLanguageCode);
		}

		var resolvedCulture = new CultureInfo(resolvedLanguageCode);
		CultureInfo.CurrentCulture = resolvedCulture;
		CultureInfo.CurrentUICulture = resolvedCulture;
		CultureInfo.DefaultThreadCurrentCulture = resolvedCulture;
		CultureInfo.DefaultThreadCurrentUICulture = resolvedCulture;

		builder.Services.AddSingleton<IAppStorageProvider, MauiAppStorageProvider>();
		builder.Services.AddSingleton<ILanguagePreferenceService, MauiLanguagePreferenceService>();
#if ANDROID
		builder.Services.AddSingleton<IPdfPageRenderer, PiccoloReader.Platforms.Android.PdfPageRenderer>();
#elif IOS
		builder.Services.AddSingleton<IPdfPageRenderer, PiccoloReader.Platforms.iOS.PdfPageRenderer>();
#endif
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
		builder.Services.AddSingleton<AnnotationService>();
		builder.Services.AddSingleton<BookmarkService>();

		builder.Services.AddTransient<LibraryViewModel>();
		builder.Services.AddTransient<FolderViewModel>();
		builder.Services.AddTransient<SheetViewerViewModel>();
		builder.Services.AddTransient<SettingsViewModel>();

		builder.Services.AddTransient<LibraryPage>();
		builder.Services.AddTransient<FolderPage>();
		builder.Services.AddTransient<SheetViewerPage>();
		builder.Services.AddTransient<SettingsPage>();

		return builder.Build();
	}
}
