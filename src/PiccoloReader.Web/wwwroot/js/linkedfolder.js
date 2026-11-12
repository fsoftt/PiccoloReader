// Linked folder (File System Access API, Chromium): a user-chosen directory that mirrors
// the library (PDFs + snapshot). The handle is persisted in IndexedDB.
const DB = "piccolo-linked-folder", STORE = "handles", KEY = "root";
let root = null;

function idb() {
    return new Promise((res, rej) => {
        const r = indexedDB.open(DB, 1);
        r.onupgradeneeded = () => r.result.createObjectStore(STORE);
        r.onsuccess = () => res(r.result);
        r.onerror = () => rej(r.error);
    });
}
async function idbGet() { const db = await idb(); return new Promise((res, rej) => { const q = db.transaction(STORE).objectStore(STORE).get(KEY); q.onsuccess = () => res(q.result); q.onerror = () => rej(q.error); }); }
async function idbSet(v) { const db = await idb(); return new Promise((res, rej) => { const tx = db.transaction(STORE, "readwrite"); v ? tx.objectStore(STORE).put(v, KEY) : tx.objectStore(STORE).delete(KEY); tx.oncomplete = res; tx.onerror = () => rej(tx.error); }); }

export function supported() { return "showDirectoryPicker" in window; }

// Returns { name, state } ("granted" | "prompt" | "denied") of the stored folder, or null.
export async function restore() {
    if (!supported()) return null;
    try {
        root = (await idbGet()) ?? null;
        if (!root) return null;
        return { name: root.name, state: await root.queryPermission({ mode: "readwrite" }) };
    } catch { return null; }
}

export async function pick() {
    try {
        const h = await window.showDirectoryPicker({ id: "piccolo-library", mode: "readwrite" });
        root = h;
        await idbSet(h);
        return { name: h.name, state: "granted" };
    } catch (e) { if (e.name === "AbortError") return null; throw e; }
}

export async function permission() {
    try { return root ? await root.queryPermission({ mode: "readwrite" }) : "denied"; } catch { return "denied"; }
}

// Must run from a user gesture.
export async function requestAccess() {
    if (!root) return "denied";
    try { return await root.requestPermission({ mode: "readwrite" }); } catch { return "denied"; }
}

export async function forget() { root = null; await idbSet(null); }

async function dirOf(parts, create) {
    let d = root;
    for (const p of parts) d = await d.getDirectoryHandle(p, { create });
    return d;
}
function split(path) { const p = path.split("/"); return [p.slice(0, -1), p[p.length - 1]]; }

export async function list() {
    const out = [];
    async function walk(dir, prefix) {
        for await (const [name, h] of dir.entries()) {
            if (h.kind === "directory") await walk(h, prefix + name + "/");
            else out.push(prefix + name);
        }
    }
    await walk(root, "");
    return out;
}

export async function read(path) {
    try {
        const [d, f] = split(path);
        const h = await (await dirOf(d, false)).getFileHandle(f);
        return new Uint8Array(await (await h.getFile()).arrayBuffer());
    } catch (e) { if (e.name === "NotFoundError" || e.name === "TypeMismatchError") return null; throw e; }
}

export async function write(path, bytes) {
    const [d, f] = split(path);
    const h = await (await dirOf(d, true)).getFileHandle(f, { create: true });
    const w = await h.createWritable();
    await w.write(bytes);
    await w.close();
}

export async function exists(path) {
    try { const [d, f] = split(path); await (await dirOf(d, false)).getFileHandle(f); return true; }
    catch { return false; }
}

export async function remove(path) {
    try { const [d, f] = split(path); await (await dirOf(d, false)).removeEntry(f); return true; }
    catch { return false; }
}

export async function removeEmptyDir(path) {
    try {
        const parts = path.split("/").filter(Boolean);
        const name = parts.pop();
        const parent = await dirOf(parts, false);
        const dir = await parent.getDirectoryHandle(name);
        for await (const _ of dir.entries()) return false;
        await parent.removeEntry(name);
        return true;
    } catch { return false; }
}
