// PWA glue: service worker registration and update flow, install prompt, storage
// usage, incoming files (drag-drop, launch queue, share target) and backup download.

let ref = null;
let waiting = null;
let installEvent = null;
const incoming = [];

function notify(name) { try { ref?.invokeMethodAsync(name); } catch { } }

// ---- service worker ----
export async function init(dotnetRef) {
    ref = dotnetRef;
    window.addEventListener("beforeinstallprompt", e => { e.preventDefault(); installEvent = e; notify("OnInstallStateChanged"); });
    window.addEventListener("appinstalled", () => { installEvent = null; notify("OnInstallStateChanged"); });
    document.addEventListener("visibilitychange", () => { try { ref?.invokeMethodAsync("OnVisibilityChanged", document.visibilityState === "hidden"); } catch { } });
    setupDrop();
    setupLaunchQueue();
    if (!("serviceWorker" in navigator)) return;
    try {
        const reg = await navigator.serviceWorker.register(new URL("service-worker.js", document.baseURI).href);
        const track = sw => sw.addEventListener("statechange", () => {
            if (sw.state === "installed" && navigator.serviceWorker.controller) { waiting = sw; notify("OnUpdateReady"); }
        });
        if (reg.waiting && navigator.serviceWorker.controller) { waiting = reg.waiting; notify("OnUpdateReady"); }
        reg.addEventListener("updatefound", () => reg.installing && track(reg.installing));
        // Look for a new version whenever the app comes back to the foreground.
        document.addEventListener("visibilitychange", () => { if (document.visibilityState === "visible") reg.update().catch(() => { }); });
    } catch (e) { console.warn("Service worker registration failed", e); }
}

export function applyUpdate() {
    if (!waiting) { location.reload(); return; }
    navigator.serviceWorker.addEventListener("controllerchange", () => location.reload(), { once: true });
    waiting.postMessage("SKIP_WAITING");
}

// ---- install ----
export function canInstall() { return !!installEvent; }
// iPhone, iPad and iPadOS reporting itself as a Mac with a touch screen.
export function isIos() {
    return /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
}
export function isStandalone() { return matchMedia("(display-mode: standalone)").matches || navigator.standalone === true; }
export async function promptInstall() {
    if (!installEvent) return false;
    const e = installEvent; installEvent = null;
    await e.prompt();
    const { outcome } = await e.userChoice;
    notify("OnInstallStateChanged");
    return outcome === "accepted";
}

// ---- storage ----
export async function storageInfo() {
    const est = navigator.storage?.estimate ? await navigator.storage.estimate() : {};
    const persisted = navigator.storage?.persisted ? await navigator.storage.persisted() : false;
    return { usage: est.usage ?? 0, quota: est.quota ?? 0, persisted, canPersist: !!navigator.storage?.persist };
}

// ---- incoming files ----
function hasFiles(e) { return e.dataTransfer && Array.from(e.dataTransfer.types || []).includes("Files"); }

function setupDrop() {
    let depth = 0;
    const on = v => document.body.classList.toggle("dragging", v);
    window.addEventListener("dragenter", e => { if (hasFiles(e)) { depth++; on(true); } });
    window.addEventListener("dragleave", e => { if (hasFiles(e) && --depth <= 0) { depth = 0; on(false); } });
    window.addEventListener("dragover", e => { if (hasFiles(e)) e.preventDefault(); });
    window.addEventListener("drop", e => {
        if (!hasFiles(e)) return;
        e.preventDefault(); depth = 0; on(false);
        const files = Array.from(e.dataTransfer.files).filter(f => /\.pdf$/i.test(f.name) || f.type === "application/pdf");
        if (files.length) { incoming.push(...files); notify("OnIncomingFiles"); }
    });
}

function setupLaunchQueue() {
    if (!("launchQueue" in window)) return;
    launchQueue.setConsumer(async params => {
        const files = [];
        for (const h of params.files || []) { try { files.push(await h.getFile()); } catch { } }
        if (files.length) { incoming.push(...files); notify("OnIncomingFiles"); }
    });
}

// Files the service worker stored from a Web Share Target POST.
export async function collectShared() {
    try {
        if (!(await caches.has("piccolo-share"))) return;
        const cache = await caches.open("piccolo-share");
        for (const req of await cache.keys()) {
            const res = await cache.match(req);
            const blob = await res.blob();
            const name = decodeURIComponent(res.headers.get("x-file-name") || "shared.pdf");
            incoming.push(new File([blob], name, { type: "application/pdf" }));
            await cache.delete(req);
        }
        if (incoming.length) notify("OnIncomingFiles");
    } catch (e) { console.warn("Share target read failed", e); }
}

export function incomingCount() { return incoming.length; }
export function incomingName() { return incoming.length ? incoming[0].name : null; }
export async function takeIncoming() {
    const f = incoming.shift();
    return f ? new Uint8Array(await f.arrayBuffer()) : null;
}

// ---- backup download (chunks are appended synchronously from the zip writer) ----
let parts = [];
export function downloadBegin() { parts = []; }
export function downloadChunk(bytes) { parts.push(bytes.slice()); }
export function downloadEnd(name) {
    const url = URL.createObjectURL(new Blob(parts, { type: "application/zip" }));
    parts = [];
    const a = document.createElement("a");
    a.href = url; a.download = name; document.body.appendChild(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60000);
}

// Preferences ("piccolo.*" keys) for the backup.
export function localKeys() {
    const out = {};
    for (let i = 0; i < localStorage.length; i++) { const k = localStorage.key(i); if (k.startsWith("piccolo.")) out[k] = localStorage.getItem(k); }
    return JSON.stringify(out);
}

// ---- coach marks ----
export function setDropText(text) { document.body.dataset.drop = text; }
// [x, y, w, h, bubbleHeight, viewportWidth, viewportHeight]; w = 0 when the target is missing.
export function coachMeasure(selector) {
    const t = document.querySelector(selector);
    if (t) t.scrollIntoView({ block: "nearest" });
    const r = t?.getBoundingClientRect();
    const b = document.querySelector(".coach-bubble")?.getBoundingClientRect();
    return [r?.x ?? 0, r?.y ?? 0, r?.width ?? 0, r?.height ?? 0, b?.height ?? 150, innerWidth, innerHeight];
}
