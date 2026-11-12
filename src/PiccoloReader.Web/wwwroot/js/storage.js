// OPFS helpers. All file I/O is async; names may contain "/" (sub-directories).
async function dirFor(parts, create) {
    let dir = await navigator.storage.getDirectory();
    for (const p of parts) dir = await dir.getDirectoryHandle(p, { create });
    return dir;
}

export async function requestPersist() {
    try { return navigator.storage?.persist ? await navigator.storage.persist() : false; }
    catch { return false; }
}

export async function writeFile(name, data) {
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

export async function deleteFile(name) {
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
