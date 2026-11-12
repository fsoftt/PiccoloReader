using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using PiccoloReader.Core.Data;
using PiccoloReader.Core.Services;
using PiccoloReader.Core.Services.Concert;
using PiccoloReader.Core.Services.Metronome;
using PiccoloReader.Core.Services.Piano;
using PiccoloReader.Core.Services.Tuner;
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
builder.Services.AddSingleton<AnnotInterop>();
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
builder.Services.AddSingleton<IMetronomePreferenceService, LocalStorageMetronomePreferenceService>();
builder.Services.AddSingleton<IPianoPreferenceService, LocalStoragePianoPreferenceService>();
builder.Services.AddSingleton<ITunerPreferenceService, LocalStorageTunerPreferenceService>();
builder.Services.AddSingleton<ITunerStripPreferenceService, LocalStorageTunerStripPreferenceService>();

// Audio tools: the worklet, the SoundFont and the microphone are only touched once a tool opens.
builder.Services.AddSingleton<WebAudio>();
builder.Services.AddSingleton<WebPianoOutput>();
builder.Services.AddSingleton<IPianoAudioOutput>(sp => sp.GetRequiredService<WebPianoOutput>());
builder.Services.AddSingleton<IMetronomePlayer, WebMetronomePlayer>();
builder.Services.AddSingleton<ITunerInput, WebTunerInput>();
builder.Services.AddSingleton<IMicrophonePermissionService, WebMicrophonePermission>();
builder.Services.AddSingleton<IPianoService>(sp =>
{
    var audio = sp.GetRequiredService<WebAudio>();
    var baseUri = sp.GetRequiredService<NavigationManager>().BaseUri;
    return new PianoService(sp.GetRequiredService<IPianoAudioOutput>(), async () =>
    {
        // Worklet first (Start needs it), then the SoundFont from the HTTP/Cache API cache.
        await audio.EnsureReadyAsync();
        return new MemoryStream(await audio.FetchCachedAsync(baseUri + "sf2/piano.sf2"), writable: false);
    });
});
builder.Services.AddSingleton(sp => new MetronomeViewModel(sp.GetRequiredService<IMetronomePlayer>(), sp.GetRequiredService<IMetronomePreferenceService>()));
builder.Services.AddSingleton(sp => new PianoViewModel(sp.GetRequiredService<IPianoService>(), sp.GetRequiredService<IPianoPreferenceService>(), sp.GetRequiredService<ITunerPreferenceService>()));
builder.Services.AddSingleton(sp => new TunerViewModel(sp.GetRequiredService<ITunerInput>(), sp.GetRequiredService<ITunerPreferenceService>(), sp.GetRequiredService<IMicrophonePermissionService>()));
builder.Services.AddSingleton(sp => new TunerStripController(sp.GetRequiredService<ITunerInput>(), sp.GetRequiredService<ITunerPreferenceService>(),
    sp.GetRequiredService<ITunerStripPreferenceService>(), sp.GetRequiredService<IMicrophonePermissionService>(), a => a()));
builder.Services.AddSingleton<IAnnotationToolPreferenceService, LocalStorageAnnotationToolPreferenceService>();
builder.Services.AddSingleton<PiccoloReader.Core.Services.Stylus.IStylusPreferenceService, LocalStorageStylusPreferenceService>();

// Core services and view models, reused as they are
builder.Services.AddSingleton<IConcertQueue, ConcertQueue>();
builder.Services.AddSingleton(sp => new LibraryService(sp.GetRequiredService<AppDatabase>(), sp.GetRequiredService<IAppStorageProvider>()));
builder.Services.AddSingleton(sp => new PdfImportService(sp.GetRequiredService<AppDatabase>(), sp.GetRequiredService<IAppStorageProvider>()));
builder.Services.AddSingleton(sp => new BookmarkService(sp.GetRequiredService<AppDatabase>()));
builder.Services.AddSingleton(sp => new PageCropService(sp.GetRequiredService<AppDatabase>()));
builder.Services.AddSingleton(sp => new AnnotationService(sp.GetRequiredService<AppDatabase>()));
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
