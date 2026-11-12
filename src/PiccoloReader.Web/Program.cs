using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PiccoloReader.Core.Data;
using PiccoloReader.Core.Services;
using PiccoloReader.Web;
using PiccoloReader.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton<BrowserInterop>();
builder.Services.AddSingleton<IAppStorageProvider, WebStorageProvider>();
builder.Services.AddSingleton<ILanguagePreferenceService, LocalStorageLanguagePreferenceService>();
builder.Services.AddSingleton<PdfCanvasRenderer>();
builder.Services.AddSingleton<PersistentDatabase>();
builder.Services.AddSingleton(sp => sp.GetRequiredService<PersistentDatabase>().Database);

var host = builder.Build();

// Ask the browser not to evict OPFS/localStorage, restore the DB, then start.
var interop = host.Services.GetRequiredService<BrowserInterop>();
await interop.RequestPersistentStorageAsync();
await host.Services.GetRequiredService<PersistentDatabase>().InitializeAsync();

await host.RunAsync();
