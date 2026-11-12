// Offline app shell. service-worker-assets.js (generated at publish) lists every published
// file with a hash; each build gets its own cache, the old one is dropped on activate.
self.importScripts("./service-worker-assets.js");
self.addEventListener("install", event => event.waitUntil(onInstall()));
self.addEventListener("activate", event => event.waitUntil(onActivate()));
self.addEventListener("message", event => { if (event.data === "SKIP_WAITING") self.skipWaiting(); });
self.addEventListener("fetch", event => event.respondWith(onFetch(event)));

const cacheNamePrefix = "piccolo-shell-";
const cacheName = cacheNamePrefix + self.assetsManifest.version;
const shareCache = "piccolo-share";
// The app can live under any base path (/ locally, /PiccoloReader/app/ on Pages): use the scope.
const baseUrl = new URL(self.registration.scope);
const include = [/\.dll$/, /\.pdb$/, /\.wasm$/, /\.html$/, /\.js$/, /\.mjs$/, /\.json$/, /\.css$/, /\.woff2?$/, /\.ttf$/, /\.png$/, /\.svg$/, /\.ico$/, /\.webmanifest$/, /\.blat$/, /\.dat$/];
// The SoundFont (7.9 MB) has its own lazy cache; the SW files themselves are never cached.
const exclude = [/service-worker/, /\.sf2$/];
const manifestUrls = new Set(self.assetsManifest.assets.map(a => new URL(a.url, baseUrl).href));

async function onInstall() {
    const assets = self.assetsManifest.assets
        .filter(a => include.some(p => p.test(a.url)) && !exclude.some(p => p.test(a.url)))
        .map(a => new Request(a.url, { integrity: a.hash, cache: "no-cache" }));
    const cache = await caches.open(cacheName);
    await cache.addAll(assets);
}

async function onActivate() {
    const keys = await caches.keys();
    await Promise.all(keys.filter(k => k.startsWith(cacheNamePrefix) && k !== cacheName).map(k => caches.delete(k)));
    await self.clients.claim();
}

async function onFetch(event) {
    const req = event.request;
    const url = new URL(req.url);
    if (req.method === "POST" && url.pathname === new URL("share-target", baseUrl).pathname) {
        return handleShare(req);
    }
    if (req.method !== "GET") return fetch(req);
    const cache = await caches.open(cacheName);
    if (req.mode === "navigate" && url.origin === baseUrl.origin && url.pathname.startsWith(baseUrl.pathname)) {
        // Any in-scope navigation (deep links included) is served the cached shell.
        const shell = await cache.match(new URL("index.html", baseUrl).href);
        if (shell) return shell;
        return fetch(req);
    }
    if (manifestUrls.has(req.url)) {
        const hit = await cache.match(req);
        if (hit) return hit;
    }
    return fetch(req);
}

// Web Share Target: keep the shared PDFs in a cache, then open the app, which imports them.
async function handleShare(req) {
    try {
        const form = await req.formData();
        const cache = await caches.open(shareCache);
        let i = 0;
        for (const f of form.getAll("files")) {
            if (!(f instanceof File)) continue;
            await cache.put(new URL("shared/" + Date.now() + "-" + i++, baseUrl).href,
                new Response(f, { headers: { "x-file-name": encodeURIComponent(f.name), "content-type": "application/pdf" } }));
        }
    } catch (e) { /* fall through to opening the app */ }
    return Response.redirect(new URL("./?shared=1", baseUrl).href, 303);
}
