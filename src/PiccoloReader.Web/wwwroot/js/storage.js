// File store: OPFS, or IndexedDB where OPFS is missing. All file I/O is async; names may contain "/" (sub-directories).
// ---- IndexedDB backend ---------------------------------------------------------------------
// Used when OPFS is missing altogether (Safari before 15.2, some private modes, WebKit builds
// without it): each file is one Blob under its name. Same API as the OPFS functions below.
const hasOpfs = typeof navigator !== "undefined" && !!navigator.storage && typeof navigator.storage.getDirectory === "function";
let idbPromise = null;
function idb() {
    return idbPromise ??= new Promise((resolve, reject) => {
        const r = indexedDB.open("piccolo-files", 1);
        r.onupgradeneeded = () => r.result.createObjectStore("files");
        r.onsuccess = () => resolve(r.result);
        r.onerror = () => reject(r.error);
    });
}
async function idbTx(mode, fn) {
    const db = await idb();
    return await new Promise((resolve, reject) => {
        const tx = db.transaction("files", mode);
        const req = fn(tx.objectStore("files"));
        tx.oncomplete = () => resolve(req ? req.result : undefined);
        tx.onerror = () => reject(tx.error);
        tx.onabort = () => reject(tx.error);
    });
}
const idbGet = (name) => idbTx("readonly", st => st.get(name));
const idbPut = (name, entry) => idbTx("readwrite", st => st.put(entry, name));
// Entries are { chunks: Uint8Array[], size } (this IndexedDB does not take Blobs everywhere).
function joinRange(e, offset, length) {
    const end = Math.min(e.size, offset + length);
    const out = new Uint8Array(Math.max(0, end - offset));
    let pos = 0, o = 0;
    for (const c of e.chunks) {
        const s = Math.max(offset, pos), t = Math.min(end, pos + c.length);
        if (t > s) { out.set(c.subarray(s - pos, t - pos), o); o += t - s; }
        pos += c.length;
        if (pos >= end) break;
    }
    return out;
}

async function dirFor(parts, create) {
    let dir = await navigator.storage.getDirectory();
    for (const p of parts) dir = await dir.getDirectoryHandle(p, { create });
    return dir;
}

export async function requestPersist() {
    try { return navigator.storage?.persist ? await navigator.storage.persist() : false; }
    catch { return false; }
}

// ---- writing ---------------------------------------------------------------------------------
// FileSystemFileHandle.createWritable is missing in Safari (before 26): there, writes go through a
// dedicated worker that uses createSyncAccessHandle (Safari 15.2+), the only OPFS writer it has.
const hasWritable = typeof FileSystemFileHandle !== "undefined" && typeof FileSystemFileHandle.prototype.createWritable === "function";

const WORKER_SRC = `
const open = new Map(); // path -> sync access handle (staged uploads)
async function handleFor(path, create) {
    const parts = path.split("/"); const file = parts.pop();
    let dir = await navigator.storage.getDirectory();
    for (const p of parts) dir = await dir.getDirectoryHandle(p, { create });
    return await dir.getFileHandle(file, { create });
}
let chain = Promise.resolve();
self.onmessage = (e) => {
    const m = e.data;
    chain = chain.then(async () => {
        try {
            if (m.op === "write") {
                const h = await (await handleFor(m.path, true)).createSyncAccessHandle();
                try { h.truncate(0); h.write(m.data, { at: 0 }); h.flush(); } finally { h.close(); }
            } else if (m.op === "begin") {
                const h = await (await handleFor(m.path, true)).createSyncAccessHandle();
                h.truncate(0); open.set(m.path, { h, at: 0 });
            } else if (m.op === "append") {
                const o = open.get(m.path); o.h.write(m.data, { at: o.at }); o.at += m.data.byteLength;
            } else if (m.op === "end") {
                const o = open.get(m.path); if (o) { o.h.flush(); o.h.close(); open.delete(m.path); }
            }
            self.postMessage({ id: m.id });
        } catch (err) { self.postMessage({ id: m.id, error: String(err && err.name || err) + ": " + String(err && err.message || "") }); }
    });
};`;

let worker = null;
let nextId = 1;
const pending = new Map();
function callWorker(msg) {
    if (!worker) {
        worker = new Worker(URL.createObjectURL(new Blob([WORKER_SRC], { type: "text/javascript" })));
        worker.onmessage = (e) => {
            const p = pending.get(e.data.id);
            if (!p) return;
            pending.delete(e.data.id);
            e.data.error ? p.reject(new Error(e.data.error)) : p.resolve();
        };
    }
    return new Promise((resolve, reject) => { const id = nextId++; pending.set(id, { resolve, reject }); worker.postMessage({ ...msg, id }); });
}

export function writesViaWorker() { return hasOpfs && !hasWritable; }
export function storageBackend() { return !hasOpfs ? "indexeddb" : hasWritable ? "opfs" : "opfs-worker"; }

export async function writeFile(name, data) {
    if (!hasOpfs) { await idbPut(name, { chunks: [data.slice()], size: data.length }); return; }
    if (!hasWritable) { await callWorker({ op: "write", path: name, data }); return; }
    const parts = name.split("/");
    const file = parts.pop();
    const dir = await dirFor(parts, true);
    const handle = await dir.getFileHandle(file, { create: true });
    // createWritable writes to a swap file and swaps on close, so a crash keeps the old copy.
    const w = await handle.createWritable();
    await w.write(data);
    await w.close();
}

export async function readFile(name) {
    if (!hasOpfs) { const e = await idbGet(name); return e ? joinRange(e, 0, e.size) : null; }
    try {
        const parts = name.split("/");
        const file = parts.pop();
        const dir = await dirFor(parts, false);
        const handle = await dir.getFileHandle(file);
        return new Uint8Array(await (await handle.getFile()).arrayBuffer());
    } catch (e) {
        if (e.name === "NotFoundError") return null;
        throw e;
    }
}

// Streamed staging of a large upload (the backup zip) in OPFS, so it never sits in memory whole.
let stage = null;
let stageName = null;
let stageParts = [];
export async function stageBegin(name) {
    if (!hasOpfs) { stageParts = []; stageName = name; return; }
    stageName = name;
    if (!hasWritable) { await callWorker({ op: "begin", path: name }); return; }
    const parts = name.split("/");
    const file = parts.pop();
    const dir = await dirFor(parts, true);
    const handle = await dir.getFileHandle(file, { create: true });
    stage = await handle.createWritable();
}
export async function stageAppend(data) {
    if (!hasOpfs) { stageParts.push(data.slice()); return; }
    if (!hasWritable) { await callWorker({ op: "append", path: stageName, data }); return; }
    await stage.write(data);
}
export async function stageEnd() {
    if (!hasOpfs) { await idbPut(stageName, { chunks: stageParts, size: stageParts.reduce((n, c) => n + c.length, 0) }); stageParts = []; return; }
    if (!hasWritable) { await callWorker({ op: "end", path: stageName }); return; }
    const w = stage; stage = null; if (w) await w.close();
}

export async function fileSize(name) {
    if (!hasOpfs) { const e = await idbGet(name); return e ? e.size : -1; }
    try {
        const parts = name.split("/");
        const file = parts.pop();
        const dir = await dirFor(parts, false);
        return (await (await dir.getFileHandle(file)).getFile()).size;
    } catch (e) {
        if (e.name === "NotFoundError") return -1;
        throw e;
    }
}

// Reads one byte range without loading the file.
export async function readRange(name, offset, length) {
    if (!hasOpfs) { const e = await idbGet(name); return joinRange(e, offset, length); }
    const parts = name.split("/");
    const file = parts.pop();
    const dir = await dirFor(parts, false);
    const f = await (await dir.getFileHandle(file)).getFile();
    return new Uint8Array(await f.slice(offset, offset + length).arrayBuffer());
}

export async function deleteFile(name) {
    if (!hasOpfs) { await idbTx("readwrite", st => st.delete(name)); return; }
    try {
        const parts = name.split("/");
        const file = parts.pop();
        const dir = await dirFor(parts, false);
        await dir.removeEntry(file);
    } catch (e) {
        if (e.name !== "NotFoundError") throw e;
    }
}

export function getLanguage() {
    return (navigator.language || "en").toLowerCase();
}

// Flush pending writes when the page is hidden or closed, so the 300 ms
// debounce cannot lose the last change. ref is a DotNetObjectReference.
let flushRef = null;
export function registerFlush(ref) {
    flushRef = ref;
    const flush = () => { try { flushRef?.invokeMethodAsync("FlushAsync"); } catch { } };
    document.addEventListener("visibilitychange", () => { if (document.visibilityState === "hidden") flush(); });
    window.addEventListener("pagehide", flush);
}
