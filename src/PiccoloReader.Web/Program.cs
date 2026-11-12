using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using PiccoloReader.Core.Data;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.Services.Concert;
using PiccoloReader.Core.ViewModels;
using PiccoloReader.Web;
using PiccoloReader.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Platform services
builder.Services.AddSingleton<BrowserInterop>();
builder.Services.AddSingleton<IAppStorageProvider, WebStorageProvider>();
builder.Services.AddSingleton<PdfCanvasRenderer>();
builder.Services.AddSingleton<ViewerInterop>();
builder.Services.AddSingleton<PersistentDatabase>();
builder.Services.AddSingleton(sp => sp.GetRequiredService<PersistentDatabase>().Database);
builder.Services.AddSingleton<UiService>();
builder.Services.AddSingleton<AppCulture>();
builder.Services.AddSingleton<WebLibrary>();

// Core preference interfaces over localStorage
builder.Services.AddSingleton<ILanguagePreferenceService, LocalStorageLanguagePreferenceService>();
builder.Services.AddSingleton<IReadingPreferenceService, LocalStorageReadingPreferenceService>();
builder.Services.AddSingleton<ISortPreferenceService, LocalStorageSortPreferenceService>();
builder.Services.AddSingleton<IConcertModePreferenceService, LocalStorageConcertModePreferenceService>();

// Core services and view models, reused as they are
builder.Services.AddSingleton<IConcertQueue, ConcertQueue>();
builder.Services.AddSingleton(sp => new LibraryService(sp.GetRequiredService<AppDatabase>(), sp.GetRequiredService<IAppStorageProvider>()));
builder.Services.AddSingleton(sp => new PdfImportService(sp.GetRequiredService<AppDatabase>(), sp.GetRequiredService<IAppStorageProvider>()));
builder.Services.AddSingleton(sp => new BookmarkService(sp.GetRequiredService<AppDatabase>()));
builder.Services.AddSingleton(sp => new PageCropService(sp.GetRequiredService<AppDatabase>()));
builder.Services.AddTransient<LibraryViewModel>();
builder.Services.AddTransient<FolderViewModel>();

var host = builder.Build();

// Ask the browser not to evict OPFS/localStorage, restore the DB, apply the language, then start.
var interop = host.Services.GetRequiredService<BrowserInterop>();
await interop.RequestPersistentStorageAsync();
var persist = host.Services.GetRequiredService<PersistentDatabase>();
await persist.InitializeAsync();
await host.Services.GetRequiredService<AppCulture>().InitializeAsync();
// Write the DB at once when the tab is hidden or closed instead of waiting for the debounce.
await interop.RegisterFlushAsync(DotNetObjectReference.Create(persist));

await host.RunAsync();
