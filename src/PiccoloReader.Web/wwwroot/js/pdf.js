import * as pdfjsLib from "../lib/pdfjs/pdf.min.mjs";
import { readFile, writeFile, deleteFile } from "./storage.js";

pdfjsLib.GlobalWorkerOptions.workerSrc = new URL("../lib/pdfjs/pdf.worker.min.mjs", import.meta.url).href;

const docs = new Map(); // stored name -> PDFDocumentProxy
const tasks = new WeakMap(); // canvas -> RenderTask

async function load(name, bytes) {
    // pdf.js transfers the buffer to its worker, so always hand it a copy.
    const doc = await pdfjsLib.getDocument({ data: bytes.slice() }).promise;
    docs.set(name, doc);
    return doc;
}

export async function importPdf(name, bytes) {
    const doc = await load(name, bytes);
    await writeFile("sheets/" + name, bytes);
    return doc.numPages;
}

export async function openPdf(name) {
    let doc = docs.get(name);
    if (!doc) {
        const bytes = await readFile("sheets/" + name);
        if (!bytes) throw new Error("PDF file missing from browser storage.");
        doc = await load(name, bytes);
    }
    return doc.numPages;
}

// [w0, h0, w1, h1, ...] in PDF points (rotation applied).
export async function getPageSizes(name) {
    const doc = docs.get(name);
    const out = [];
    for (let i = 1; i <= doc.numPages; i++) {
        const v = (await doc.getPage(i)).getViewport({ scale: 1 });
        out.push(v.width, v.height);
    }
    return out;
}

// Renders a page (or the cropped part of it, crop = [l, t, r, b] normalized to
// the full page) so that it is cssWidth CSS pixels wide.
export async function renderPage(name, pageIndex, canvas, cssWidth, crop) {
    const doc = docs.get(name);
    const page = await doc.getPage(pageIndex + 1);
    const base = page.getViewport({ scale: 1 });
    const [l, t, r, b] = crop ?? [0, 0, 1, 1];
    const visW = base.width * (r - l);
    const visH = base.height * (b - t);
    let dpr = Math.min(window.devicePixelRatio || 1, 2.5);
    let scale = (cssWidth / visW) * dpr;
    const maxPixels = 16e6;
    const px = visW * scale * visH * scale;
    if (px > maxPixels) scale *= Math.sqrt(maxPixels / px);
    const viewport = page.getViewport({ scale, offsetX: -l * base.width * scale, offsetY: -t * base.height * scale });
    const w = Math.max(1, Math.floor(visW * scale));
    const h = Math.max(1, Math.floor(visH * scale));
    const prev = tasks.get(canvas);
    if (prev) { try { prev.cancel(); } catch { } }
    // Render offscreen first so a cancelled or slow render never shows a blank canvas.
    const off = document.createElement("canvas");
    off.width = w; off.height = h;
    const task = page.render({ canvasContext: off.getContext("2d"), viewport, canvas: off });
    tasks.set(canvas, task);
    try { await task.promise; } catch (e) { if (e?.name === "RenderingCancelledException") return false; throw e; }
    if (tasks.get(canvas) !== task) return false;
    canvas.width = w; canvas.height = h;
    canvas.getContext("2d").drawImage(off, 0, 0);
    canvas.style.width = cssWidth + "px";
    canvas.style.aspectRatio = w + " / " + h;
    return true;
}

// ---- thumbnails (first page, small, cached in OPFS) ----
const thumbUrls = new Map();
let thumbQueue = Promise.resolve();

export function thumbnailUrl(name, id) {
    const run = thumbQueue.then(() => makeThumb(name, id), () => makeThumb(name, id));
    thumbQueue = run.catch(() => { });
    return run;
}

async function makeThumb(name, id) {
    if (thumbUrls.has(id)) return thumbUrls.get(id);
    let bytes = await readFile("thumbs/" + id + ".png");
    let blob;
    if (bytes) {
        blob = new Blob([bytes], { type: "image/png" });
    } else {
        const src = await readFile("sheets/" + name);
        if (!src) return null;
        const doc = await pdfjsLib.getDocument({ data: src }).promise;
        try {
            const page = await doc.getPage(1);
            const base = page.getViewport({ scale: 1 });
            const scale = 160 / base.width;
            const viewport = page.getViewport({ scale });
            const c = document.createElement("canvas");
            c.width = Math.floor(viewport.width); c.height = Math.floor(viewport.height);
            await page.render({ canvasContext: c.getContext("2d"), viewport, canvas: c }).promise;
            blob = await new Promise(res => c.toBlob(res, "image/png"));
            await writeFile("thumbs/" + id + ".png", new Uint8Array(await blob.arrayBuffer()));
        } finally { doc.destroy(); }
    }
    const url = URL.createObjectURL(blob);
    thumbUrls.set(id, url);
    return url;
}

export async function deleteSheetFiles(name, id) {
    const d = docs.get(name);
    if (d) { docs.delete(name); try { d.destroy(); } catch { } }
    const u = thumbUrls.get(id);
    if (u) { URL.revokeObjectURL(u); thumbUrls.delete(id); }
    await deleteFile("sheets/" + name);
    await deleteFile("thumbs/" + id + ".png");
}
