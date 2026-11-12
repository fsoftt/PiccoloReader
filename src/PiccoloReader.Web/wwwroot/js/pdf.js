import * as pdfjsLib from "../lib/pdfjs/pdf.min.mjs";
import { readFile, writeFile } from "./storage.js";

pdfjsLib.GlobalWorkerOptions.workerSrc = new URL("../lib/pdfjs/pdf.worker.min.mjs", import.meta.url).href;

const docs = new Map(); // stored name -> PDFDocumentProxy
let renderTask = null;

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

export async function renderPage(name, pageIndex, canvas, maxW, maxH) {
    const doc = docs.get(name);
    const page = await doc.getPage(pageIndex + 1);
    const base = page.getViewport({ scale: 1 });
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const fit = Math.min(maxW / base.width, maxH / base.height, 3);
    const viewport = page.getViewport({ scale: fit * dpr });
    canvas.width = Math.floor(viewport.width);
    canvas.height = Math.floor(viewport.height);
    if (renderTask) { try { renderTask.cancel(); } catch { } }
    renderTask = page.render({ canvasContext: canvas.getContext("2d"), viewport, canvas });
    try { await renderTask.promise; } catch (e) { if (e?.name !== "RenderingCancelledException") throw e; }
}

let keyHandler = null;
export function registerKeys(ref) {
    unregisterKeys();
    keyHandler = (e) => {
        if (["ArrowRight", "ArrowDown", "PageDown"].includes(e.key)) { e.preventDefault(); ref.invokeMethodAsync("NextAsync"); }
        else if (["ArrowLeft", "ArrowUp", "PageUp"].includes(e.key)) { e.preventDefault(); ref.invokeMethodAsync("PrevAsync"); }
    };
    window.addEventListener("keydown", keyHandler);
}

export function unregisterKeys() {
    if (keyHandler) window.removeEventListener("keydown", keyHandler);
    keyHandler = null;
}
