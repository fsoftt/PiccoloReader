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
