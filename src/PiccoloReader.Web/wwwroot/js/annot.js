// Annotation layer over the PDF canvases: paints the Core annotation model with
// Canvas 2D (same geometry rules as the Android AnnotationPainter), turns pointer
// input (pen, touch, mouse) into strokes / erasing / selection, and writes the
// annotated PDF. Annotation data lives in .NET (Core); this module only draws and
// reports gestures.
import { renderForExport } from "./pdf.js";
import { readFile } from "./storage.js";

const TEXT_FONT = '"OpenSans", system-ui, sans-serif';
const BRAVURA = "Bravura";
const layers = new Map(); // container element -> layer
const pageItems = new Map(); // page index -> items
const requested = new Set();
let host = null; // { ref, stage }
let fontsPromise = null;

const opts = {
    tool: "none", // none | pencil | highlighter | eraser | icons | text
    editing: false,
    stylusDirect: true,
    pencil: { color: "#000", w: 0.002 },
    hl: { color: "rgba(255,235,59,.4)", w: 0.025 },
    eraser: { r: 0.05 },
};

// ------------------------------------------------------------ fonts
export function ensureFonts() {
    if (fontsPromise) return fontsPromise;
    fontsPromise = (async () => {
        try {
            await Promise.all([
                document.fonts.load('100px "OpenSans"'),
                document.fonts.load('100px "' + BRAVURA + '"', ""),
            ]);
        } catch { }
        glyphBounds.clear();
        for (const L of layers.values()) draw(L);
    })();
    return fontsPromise;
}

// ------------------------------------------------------------ painting
const glyphBounds = new Map();

function inkBounds(ctx, ch, key) {
    let b = glyphBounds.get(key);
    if (!b) {
        ctx.save();
        ctx.font = "100px " + BRAVURA;
        ctx.textBaseline = "alphabetic"; ctx.textAlign = "left";
        const m = ctx.measureText(ch);
        b = { w: m.actualBoundingBoxLeft + m.actualBoundingBoxRight, h: m.actualBoundingBoxAscent + m.actualBoundingBoxDescent };
        ctx.restore();
        if (b.w > 0 && b.h > 0) glyphBounds.set(key, b);
    }
    return b;
}

// Fits a glyph's ink into the target rectangle, like AnnotationPainter.DrawGlyphFitted.
function drawGlyphFitted(ctx, cp, x, y, w, h, vs, color) {
    if (w <= 0 || h <= 0) return;
    const ch = String.fromCodePoint(cp);
    const b = inkBounds(ctx, ch, cp);
    if (!b || b.w <= 0 || b.h <= 0) return;
    const scale = Math.min(w / b.w, h / b.h) * 0.9 * (vs || 1);
    ctx.save();
    ctx.font = 100 * scale + "px " + BRAVURA;
    ctx.textBaseline = "alphabetic"; ctx.textAlign = "left";
    ctx.fillStyle = color || "#000";
    const m = ctx.measureText(ch);
    const midX = (m.actualBoundingBoxRight - m.actualBoundingBoxLeft) / 2;
    const midY = (m.actualBoundingBoxDescent - m.actualBoundingBoxAscent) / 2;
    ctx.fillText(ch, x + w / 2 - midX, y + h / 2 - midY);
    ctx.restore();
}

// Crescent between the two edges, as in SlurGeometry.Edges. p = 4 control points (pixels).
function slurEdges(p, thickness, tipFactor) {
    const dx = p[3][0] - p[0][0], dy = p[3][1] - p[0][1];
    const len = Math.hypot(dx, dy);
    const nx = len > 0 ? -dy / len : 0, ny = len > 0 ? dx / len : 0;
    const tip = thickness / 2 * tipFactor;
    const shift = (thickness / 2 - 0.25 * tip) / 0.75;
    const side = (s) => [
        [p[0][0] + s * nx * tip, p[0][1] + s * ny * tip],
        [p[1][0] + s * nx * shift, p[1][1] + s * ny * shift],
        [p[2][0] + s * nx * shift, p[2][1] + s * ny * shift],
        [p[3][0] + s * nx * tip, p[3][1] + s * ny * tip],
    ];
    return [side(1), side(-1)];
}

function drawSlur(ctx, ctrl, x, y, w, h, thickness, tip, color) {
    if (w <= 0 || h <= 0) return;
    const p = [0, 1, 2, 3].map(i => [x + ctrl[2 * i] * w, y + ctrl[2 * i + 1] * h]);
    const [o, n] = slurEdges(p, thickness, tip);
    ctx.beginPath();
    ctx.moveTo(o[0][0], o[0][1]);
    ctx.bezierCurveTo(o[1][0], o[1][1], o[2][0], o[2][1], o[3][0], o[3][1]);
    ctx.lineTo(n[3][0], n[3][1]);
    ctx.bezierCurveTo(n[2][0], n[2][1], n[1][0], n[1][1], n[0][0], n[0][1]);
    ctx.closePath();
    ctx.fillStyle = color;
    ctx.fill();
}

function lineHeightPerSize(ctx) {
    ctx.save();
    ctx.font = "100px " + TEXT_FONT;
    const m = ctx.measureText("Hg");
    ctx.restore();
    const h = (m.fontBoundingBoxAscent + m.fontBoundingBoxDescent) / 100;
    return h > 0 ? h : 1.36;
}

const measureCtx = document.createElement("canvas").getContext("2d");

// Text width / line height, as drawn (what Core's TextFigure.Size needs).
export async function textAspect(text) {
    await ensureFonts();
    measureCtx.font = "100px " + TEXT_FONT;
    return measureCtx.measureText(text).width / (lineHeightPerSize(measureCtx) * 100);
}

function drawText(ctx, text, x, y, w, h, color) {
    if (w <= 0 || h <= 0 || !text) return;
    const size = h / lineHeightPerSize(ctx);
    ctx.save();
    ctx.font = size + "px " + TEXT_FONT;
    ctx.textBaseline = "alphabetic"; ctx.textAlign = "left";
    ctx.fillStyle = color;
    const m = ctx.measureText(text);
    if (m.width > 0) {
        ctx.translate(x, y);
        ctx.scale(w / m.width, 1);
        ctx.fillText(text, 0, m.fontBoundingBoxAscent);
    }
    ctx.restore();
}

function strokePath(ctx, pts, fx, fy) {
    ctx.beginPath();
    ctx.moveTo(fx(pts[0]), fy(pts[1]));
    for (let i = 2; i < pts.length; i += 2) ctx.lineTo(fx(pts[i]), fy(pts[i + 1]));
}

// Items are in page coordinates (0-1 of the FULL page), painted into a W x H canvas that
// shows the crop [l, t, r, b] of it. Stroke widths are fractions of the page width.
function paintItems(ctx, items, W, H, crop, liveStroke) {
    const [l, t, r, b] = crop || [0, 0, 1, 1];
    const sx = W / (r - l), sy = H / (b - t);
    const fx = (x) => (x - l) * sx, fy = (y) => (y - t) * sy;
    ctx.lineCap = "round"; ctx.lineJoin = "round";
    for (const it of items) {
        switch (it.k) {
            case "s":
                if (it.p.length < 4) break;
                ctx.strokeStyle = it.c; ctx.lineWidth = it.w * sx;
                strokePath(ctx, it.p, fx, fy);
                ctx.stroke();
                break;
            case "h": {
                const sw = 0.003 * sx, inset = sw / 2;
                const bx = fx(it.x) + inset, by = fy(it.y) + inset, bw = it.w * sx - sw, bh = it.h * sy - sw;
                if (bw <= 0 || bh <= 0) break;
                ctx.strokeStyle = it.c; ctx.lineWidth = sw;
                for (let i = 0; i < it.ln.length; i += 4) {
                    ctx.beginPath();
                    ctx.moveTo(bx + it.ln[i] * bw, by + it.ln[i + 1] * bh);
                    ctx.lineTo(bx + it.ln[i + 2] * bw, by + it.ln[i + 3] * bh);
                    ctx.stroke();
                }
                break;
            }
            case "l":
                drawSlur(ctx, it.cp, fx(it.x), fy(it.y), it.w * sx, it.h * sy, Math.max(it.th * sx, 2), it.tip, it.c);
                break;
            case "x":
                drawText(ctx, it.t, fx(it.x), fy(it.y), it.w * sx, it.h * sy, it.c);
                break;
            case "g":
                drawGlyphFitted(ctx, it.cp, fx(it.x), fy(it.y), it.w * sx, it.h * sy, it.vs, "#000");
                break;
        }
    }
    if (liveStroke && liveStroke.p.length >= 4) {
        ctx.strokeStyle = liveStroke.c; ctx.lineWidth = liveStroke.w * sx;
        strokePath(ctx, liveStroke.p, fx, fy);
        ctx.stroke();
    }
}

function draw(L) {
    const c = L.canvas;
    const ctx = c.getContext("2d");
    ctx.clearRect(0, 0, c.width, c.height);
    const items = pageItems.get(L.page);
    if (!items) return;
    paintItems(ctx, items, c.width, c.height, L.crop, live && live.layer === L ? live : null);
}

const rafLayers = new Set();
function schedule(L) {
    if (rafLayers.has(L)) return;
    rafLayers.add(L);
    requestAnimationFrame(() => { rafLayers.delete(L); draw(L); });
}

// Symbol picker tiles: <canvas data-tile="index"> children of the container.
export async function paintTiles(container, specs) {
    await ensureFonts();
    for (const el of container.querySelectorAll("canvas[data-tile]")) {
        const s = specs[+el.dataset.tile];
        if (!s) continue;
        const dpr = Math.min(window.devicePixelRatio || 1, 3);
        el.width = 40 * dpr; el.height = 40 * dpr;
        const ctx = el.getContext("2d");
        ctx.clearRect(0, 0, el.width, el.height);
        if (s.slur) {
            const inset = el.width * 0.15;
            drawSlur(ctx, s.ctl, inset, inset * 2, el.width - 2 * inset, el.height - 4 * inset, el.height * 0.07, 0.25, "#000");
        } else {
            drawGlyphFitted(ctx, s.cp, 0, 0, el.width, el.height, s.vs, "#000");
        }
    }
}

// ------------------------------------------------------------ layers
// Called by viewer.js after a page canvas was (re)drawn. container holds the page canvas.
export function layerRendered(container, page, crop) {
    const pageCanvas = container.querySelector("canvas:not(.annot)");
    if (!pageCanvas) return;
    let L = layers.get(container);
    if (!L) {
        const canvas = document.createElement("canvas");
        canvas.className = "annot";
        container.appendChild(canvas);
        L = { el: container, canvas, page, crop };
        layers.set(container, L);
    }
    if (sel && sel.container === container && sel.page !== page) selectNone(true);
    L.page = page; L.crop = crop || [0, 0, 1, 1];
    L.canvas.width = pageCanvas.width; L.canvas.height = pageCanvas.height;
    if (!pageItems.has(page) && host && !requested.has(page)) {
        requested.add(page);
        host.ref.invokeMethodAsync("OnNeedPage", page);
    }
    ensureFonts();
    draw(L);
    if (sel && sel.container === container) placeSelection();
}

export function layerCleared(container) {
    const L = layers.get(container);
    if (!L) return;
    L.canvas.remove();
    layers.delete(container);
    if (sel && sel.container === container) clearSelectionUi();
}

export function setPage(page, json) {
    requested.delete(page);
    pageItems.set(page, JSON.parse(json));
    for (const L of layers.values()) if (L.page === page) draw(L);
    if (sel && sel.page === page) {
        if (!pageItems.get(page).some(i => i.id === sel.id)) clearSelectionUi(); else placeSelection();
    }
}

// Visible part of the page on screen, as [l, t, r, b] in full-page coordinates (null when unknown).
export function visibleArea(page) {
    if (!host) return null;
    for (const L of layers.values()) {
        if (L.page !== page) continue;
        const r = L.el.getBoundingClientRect(), s = host.stage.getBoundingClientRect();
        const x0 = Math.max(r.left, s.left), x1 = Math.min(r.right, s.right);
        const y0 = Math.max(r.top, s.top), y1 = Math.min(r.bottom, s.bottom);
        if (x1 <= x0 || y1 <= y0) return null;
        const [l, t, rr, bb] = L.crop;
        return [l + (x0 - r.left) / r.width * (rr - l), t + (y0 - r.top) / r.height * (bb - t),
                l + (x1 - r.left) / r.width * (rr - l), t + (y1 - r.top) / r.height * (bb - t)];
    }
    return null;
}

// ------------------------------------------------------------ options
function applyStageClasses() {
    if (!host) return;
    const drawing = opts.editing && (opts.tool === "pencil" || opts.tool === "highlighter" || opts.tool === "eraser" || opts.tool === "text");
    host.stage.classList.toggle("drawing", drawing);
    host.stage.classList.toggle("editing", opts.editing);
}

export function setOptions(tool, editing, stylusDirect, pencilColor, pencilW, hlColor, hlW, eraserR) {
    opts.tool = tool; opts.editing = editing; opts.stylusDirect = stylusDirect;
    opts.pencil = { color: pencilColor, w: pencilW };
    opts.hl = { color: hlColor, w: hlW };
    opts.eraser = { r: eraserR };
    applyStageClasses();
    if (!editing) clearSelectionUi();
}

// ------------------------------------------------------------ input
let live = null; // { layer, p, c, w }
let gesture = null;
let touchBlockedUntil = 0;
const owned = new Set(); // pointer ids whose gesture belongs to the annotation layer

export function owns(pointerId) { return owned.has(pointerId); }

// Palm rejection: while a pen is in contact (or was just lifted), touches are not for the page.
export function rejectTouch(e) {
    if (e.pointerType !== "touch" || !opts.stylusDirect) return false;
    return !!(gesture && gesture.pen) || performance.now() < touchBlockedUntil;
}

function layerAt(target) {
    let el = target;
    while (el && el !== host.stage) {
        const L = layers.get(el);
        if (L) return L;
        el = el.parentElement;
    }
    return null;
}

function toPage(L, e) {
    const r = L.el.getBoundingClientRect();
    const [l, t, rr, bb] = L.crop;
    return [l + (e.clientX - r.left) / r.width * (rr - l), t + (e.clientY - r.top) / r.height * (bb - t)];
}

function roleFor(e) {
    const pen = e.pointerType === "pen";
    if (pen) {
        const eraserEnd = e.button === 5 || (e.buttons & 32) !== 0;
        const barrel = (e.buttons & 2) !== 0 || e.button === 2;
        if ((eraserEnd || barrel) && (opts.stylusDirect || opts.editing)) return "eraser";
    }
    if (opts.editing) {
        if (opts.tool === "pencil" || opts.tool === "highlighter" || opts.tool === "eraser") return opts.tool;
        if (pen && opts.stylusDirect) return "pencil";
        if (opts.tool === "text") return "text";
        return "select";
    }
    if (pen && opts.stylusDirect) return "pencil";
    return null;
}

export function attachInput(ref, stage) {
    detachInput();
    host = { ref, stage };
    const h = {
        down: (e) => onDown(e),
        move: (e) => onMove(e),
        up: (e) => onUp(e, false),
        cancel: (e) => onUp(e, true),
        hover: (e) => {
            if (e.pointerType !== "pen" || !host) return;
            stage.classList.add("pen");
            clearTimeout(host.penTimer);
            host.penTimer = setTimeout(() => stage.classList.remove("pen"), 1500);
        },
        ctx: (e) => { if (opts.stylusDirect || opts.editing) e.preventDefault(); },
    };
    host.h = h;
    stage.addEventListener("pointerdown", h.down, true);
    stage.addEventListener("pointermove", h.move, true);
    stage.addEventListener("pointerup", h.up, true);
    stage.addEventListener("pointercancel", h.cancel, true);
    stage.addEventListener("pointerover", h.hover, true);
    stage.addEventListener("pointermove", h.hover, true);
    stage.addEventListener("contextmenu", h.ctx);
    applyStageClasses();
}

export function detachInput() {
    if (!host) return;
    const { stage, h } = host;
    stage.removeEventListener("pointerdown", h.down, true);
    stage.removeEventListener("pointermove", h.move, true);
    stage.removeEventListener("pointerup", h.up, true);
    stage.removeEventListener("pointercancel", h.cancel, true);
    stage.removeEventListener("pointerover", h.hover, true);
    stage.removeEventListener("pointermove", h.hover, true);
    stage.removeEventListener("contextmenu", h.ctx);
    clearTimeout(host.penTimer);
    stage.classList.remove("pen", "drawing", "editing");
    host = null;
    gesture = null; live = null; owned.clear();
    clearSelectionUi();
    for (const L of layers.values()) L.canvas.remove();
    layers.clear();
    pageItems.clear(); requested.clear();
}

function onDown(e) {
    if (!host) return;
    if (e.target.closest && e.target.closest(".sel-handle")) return;
    // A second finger turns a drawing into a pinch: drop the stroke.
    if (gesture && e.pointerId !== gesture.id) {
        if (e.pointerType === "touch") cancelGesture();
        return;
    }
    if (gesture) return;
    if (rejectTouch(e)) { e.stopPropagation(); return; }
    const role = roleFor(e);
    if (!role) return;
    const L = layerAt(e.target);
    const pen = e.pointerType === "pen";
    const g = gesture = { id: e.pointerId, kind: L ? role : "none", layer: L, pen, x0: e.clientX, y0: e.clientY, t0: performance.now(), moved: false };
    owned.add(e.pointerId);
    if (pen) touchBlockedUntil = performance.now() + 600;
    if (!L) return;
    if (role === "select") { beginSelect(e, L); return; }
    if (role === "text") return;
    const [px, py] = toPage(L, e);
    try { L.canvas.setPointerCapture(e.pointerId); } catch { }
    if (role === "eraser") {
        g.pts = [px, py]; g.chain = Promise.resolve(); g.raf = 0;
        showRing(L, e);
        sendErase(g);
    } else {
        const s = role === "highlighter" ? opts.hl : opts.pencil;
        live = { layer: L, p: [px, py], c: s.color, w: s.w };
    }
}

function onMove(e) {
    if (!host || !gesture || e.pointerId !== gesture.id || gesture.done) return;
    const g = gesture;
    if (g.pen) touchBlockedUntil = performance.now() + 600;
    if (!g.moved && Math.hypot(e.clientX - g.x0, e.clientY - g.y0) > 6) {
        g.moved = true;
        if (g.kind === "pencil" || g.kind === "highlighter" || g.kind === "eraser") host.ref.invokeMethodAsync("OnDrawStart");
    }
    if (g.kind === "pencil" || g.kind === "highlighter") {
        const evs = e.getCoalescedEvents ? e.getCoalescedEvents() : [];
        for (const ce of (evs.length ? evs : [e])) {
            const [px, py] = toPage(g.layer, ce);
            const p = live.p;
            const dx = (px - p[p.length - 2]) * g.layer.canvas.width, dy = (py - p[p.length - 1]) * g.layer.canvas.height;
            if (dx * dx + dy * dy < 1) continue;
            p.push(px, py);
        }
        schedule(g.layer);
    } else if (g.kind === "eraser") {
        const [px, py] = toPage(g.layer, e);
        g.pts.push(px, py);
        moveRing(e);
        if (!g.raf) g.raf = requestAnimationFrame(() => { g.raf = 0; sendErase(g); });
    } else if (g.kind === "move" && g.moved) {
        dragMove(e);
    }
}

function onUp(e, canceled) {
    if (!host || !gesture || e.pointerId !== gesture.id) return;
    const g = gesture;
    gesture = null;
    clearTimeout(g.lp);
    hideRing();
    const dt = performance.now() - g.t0;
    const tap = !canceled && !g.moved && Math.hypot(e.clientX - g.x0, e.clientY - g.y0) < 10 && dt < 600;
    if (g.pen) touchBlockedUntil = performance.now() + 500;
    // The click that follows an owned gesture must not toggle the chrome or turn the page.
    setTimeout(() => owned.delete(g.id), 0);
    if (g.done) return;
    if (g.kind === "pencil" || g.kind === "highlighter") {
        const l = live; live = null;
        schedule(g.layer);
        if (canceled || !l) return;
        // A pen tap without travel is a tap on the page, not a stroke.
        if (!g.moved && dt < 300 && !opts.editing) { owned.delete(g.id); return; }
        if (l.p.length >= 4) host.ref.invokeMethodAsync("OnStroke", g.layer.page, l.p, g.kind);
    } else if (g.kind === "eraser") {
        sendErase(g);
        g.chain = g.chain.then(() => host && host.ref.invokeMethodAsync("OnEraseEnd", g.layer.page));
    } else if (g.kind === "text") {
        if (tap) {
            const [px, py] = toPage(g.layer, e);
            host.ref.invokeMethodAsync("OnTextTap", g.layer.page, px, py);
        }
    } else if (g.kind === "select") {
        if (tap) selectNone(true);
    } else if (g.kind === "move") {
        if (g.moved) endMove(canceled);
    }
}

function cancelGesture() {
    const g = gesture;
    if (!g) return;
    gesture = null; live = null;
    clearTimeout(g.lp);
    hideRing();
    if (g.kind === "move" && g.moved) endMove(true);
    if (g.kind === "eraser") {
        sendErase(g);
        g.chain = g.chain.then(() => host && host.ref.invokeMethodAsync("OnEraseEnd", g.layer.page));
    }
    if (g.layer) schedule(g.layer);
}

// ------------------------------------------------------------ eraser
let ring = null;
function showRing(L, e) {
    if (!ring) {
        ring = document.createElement("div");
        ring.className = "eraser-ring";
        document.body.appendChild(ring);
    }
    const r = L.el.getBoundingClientRect();
    const [l, , rr] = L.crop;
    const d = opts.eraser.r * 2 * r.width / (rr - l);
    ring.style.width = ring.style.height = d + "px";
    ring.style.display = "block";
    moveRing(e);
}
function moveRing(e) {
    if (ring) ring.style.transform = `translate(${e.clientX - parseFloat(ring.style.width) / 2}px, ${e.clientY - parseFloat(ring.style.height) / 2}px)`;
}
function hideRing() { if (ring) ring.style.display = "none"; }

// Erase points go to .NET in order (one call at a time per gesture).
function sendErase(g) {
    if (!g.pts.length) return;
    const pts = g.pts; g.pts = [];
    g.chain = g.chain.then(() => host && host.ref.invokeMethodAsync("OnErase", g.layer.page, pts));
}

// ------------------------------------------------------------ selection
let sel = null; // { page, id, container, box, handle, item }

function figureAt(L, px, py) {
    const items = pageItems.get(L.page) || [];
    for (let i = items.length - 1; i >= 0; i--) {
        const it = items[i];
        if (it.k === "s") continue;
        if (px >= it.x && px <= it.x + it.w && py >= it.y && py <= it.y + it.h) return it;
    }
    return null;
}

function beginSelect(e, L) {
    const g = gesture;
    const [px, py] = toPage(L, e);
    const hit = figureAt(L, px, py);
    if (!hit) return;
    if (!sel || sel.id !== hit.id || sel.page !== L.page) {
        selectItem(L, hit);
        host.ref.invokeMethodAsync("OnSelect", L.page, hit.id);
    }
    g.kind = "move";
    g.item = hit;
    g.start = { x: hit.x, y: hit.y, px, py };
    g.lp = setTimeout(() => {
        if (gesture === g && !g.moved) {
            g.done = true;
            host.ref.invokeMethodAsync("OnLongPress", L.page, hit.id);
        }
    }, 550);
    try { L.canvas.setPointerCapture(e.pointerId); } catch { }
}

function dragMove(e) {
    const g = gesture;
    const L = g.layer, it = g.item;
    const [px, py] = toPage(L, e);
    const [cl, ct, cr, cb] = L.crop;
    it.x = Math.min(Math.max(g.start.x + px - g.start.px, cl), Math.max(cl, cr - it.w));
    it.y = Math.min(Math.max(g.start.y + py - g.start.py, ct), Math.max(ct, cb - it.h));
    placeSelection();
    schedule(L);
}

function endMove(canceled) {
    if (!sel) return;
    const it = sel.item;
    if (canceled) { host.ref.invokeMethodAsync("OnNeedPage", sel.page); return; }
    host.ref.invokeMethodAsync("OnMoved", sel.page, it.id, it.x, it.y);
}

function selectItem(L, it) {
    clearSelectionUi();
    const box = document.createElement("div");
    box.className = "sel-box";
    box.setAttribute("data-testid", "selection");
    const handle = document.createElement("div");
    handle.className = "sel-handle";
    handle.setAttribute("data-testid", "resize-handle");
    box.appendChild(handle);
    L.el.appendChild(box);
    sel = { page: L.page, id: it.id, container: L.el, box, handle, item: it };
    handle.addEventListener("pointerdown", onHandleDown);
    placeSelection();
}

function selectNone(notify) {
    const had = !!sel;
    clearSelectionUi();
    if (had && notify) host.ref.invokeMethodAsync("OnSelect", -1, 0);
}

export function clearSelectionUi() {
    if (!sel) return;
    sel.handle.removeEventListener("pointerdown", onHandleDown);
    sel.box.remove();
    sel = null;
}

// Select a figure from .NET (a freshly placed icon or text).
export function selectById(page, id) {
    if (id === 0) { clearSelectionUi(); return; }
    const L = [...layers.values()].find(x => x.page === page);
    const it = (pageItems.get(page) || []).find(i => i.id === id);
    if (L && it) selectItem(L, it);
}

function placeSelection() {
    if (!sel) return;
    const L = layers.get(sel.container);
    if (!L) return;
    const it = (pageItems.get(sel.page) || []).find(i => i.id === sel.id) || sel.item;
    sel.item = it;
    const [l, t, r, b] = L.crop;
    const s = sel.box.style;
    s.left = (it.x - l) / (r - l) * 100 + "%";
    s.top = (it.y - t) / (b - t) * 100 + "%";
    s.width = it.w / (r - l) * 100 + "%";
    s.height = it.h / (b - t) * 100 + "%";
}

let resize = null;
function onHandleDown(e) {
    if (!sel) return;
    e.preventDefault(); e.stopPropagation();
    const L = layers.get(sel.container), it = sel.item;
    resize = { id: e.pointerId, x0: e.clientX, y0: e.clientY, w: it.w, h: it.h, x: it.x, L, it };
    owned.add(e.pointerId);
    sel.handle.setPointerCapture(e.pointerId);
    sel.handle.addEventListener("pointermove", onHandleMove);
    sel.handle.addEventListener("pointerup", onHandleUp);
    sel.handle.addEventListener("pointercancel", onHandleUp);
}
function onHandleMove(e) {
    if (!resize || e.pointerId !== resize.id) return;
    const { L, it } = resize;
    const r = L.el.getBoundingClientRect();
    const [l, t, rr, bb] = L.crop;
    const dw = (e.clientX - resize.x0) / r.width * (rr - l);
    const dh = (e.clientY - resize.y0) / r.height * (bb - t);
    let w = Math.max(0.02, resize.w + dw), h = Math.max(0.02, resize.h + dh);
    if (it.k === "x" && resize.w > 0 && resize.h > 0) {
        // Text keeps its proportions; the average of both drags scales it.
        let scale = (w / resize.w + h / resize.h) / 2;
        scale = Math.min(scale, Math.min((rr - it.x) / resize.w, (bb - it.y) / resize.h));
        h = Math.min(Math.max(resize.h * scale, 0.012), 0.2);
        w = resize.w * h / resize.h;
    } else {
        w = Math.min(w, Math.max(0.02, rr - it.x));
        h = Math.min(h, Math.max(0.02, bb - it.y));
    }
    it.w = w; it.h = h;
    placeSelection(); schedule(L);
}
function onHandleUp(e) {
    if (!resize || e.pointerId !== resize.id) return;
    const r = resize; resize = null;
    if (sel) {
        sel.handle.removeEventListener("pointermove", onHandleMove);
        sel.handle.removeEventListener("pointerup", onHandleUp);
        sel.handle.removeEventListener("pointercancel", onHandleUp);
    }
    setTimeout(() => owned.delete(r.id), 0);
    host.ref.invokeMethodAsync("OnResized", r.L.page, r.it.id, r.x, r.it.w, r.it.h, r.w, r.h);
}

// ------------------------------------------------------------ export
let exp = null;

export function exportBegin() { exp = { pages: [] }; }

export async function exportPage(name, i, json) {
    await ensureFonts();
    const items = JSON.parse(json);
    const { canvas, w, h } = await renderForExport(name, i);
    if (items.length) paintItems(canvas.getContext("2d"), items, canvas.width, canvas.height, null, null);
    const blob = await new Promise(res => canvas.toBlob(res, "image/jpeg", 0.85));
    exp.pages.push({ jpeg: new Uint8Array(await blob.arrayBuffer()), px: canvas.width, py: canvas.height, w, h });
    canvas.width = canvas.height = 1;
}

const enc = new TextEncoder();

// Minimal PDF writer: one JPEG image per page, drawn over the whole page.
function buildPdf(pages) {
    const chunks = [], offsets = [];
    let len = 0;
    const put = (data) => { const b = typeof data === "string" ? enc.encode(data) : data; chunks.push(b); len += b.length; };
    const obj = (n, body) => { offsets[n] = len; put(`${n} 0 obj\n`); put(body); put("\nendobj\n"); };
    const objStream = (n, dict, data) => { offsets[n] = len; put(`${n} 0 obj\n<< ${dict} /Length ${data.length} >>\nstream\n`); put(data); put("\nendstream\nendobj\n"); };
    put("%PDF-1.4\n");
    put(new Uint8Array([0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]));
    obj(1, "<< /Type /Catalog /Pages 2 0 R >>");
    obj(2, `<< /Type /Pages /Count ${pages.length} /Kids [${pages.map((_, i) => `${3 + i * 3} 0 R`).join(" ")}] >>`);
    const f = (v) => +v.toFixed(2);
    pages.forEach((p, i) => {
        const pg = 3 + i * 3, content = pg + 1, img = pg + 2;
        obj(pg, `<< /Type /Page /Parent 2 0 R /MediaBox [0 0 ${f(p.w)} ${f(p.h)}] /Resources << /XObject << /Im0 ${img} 0 R >> >> /Contents ${content} 0 R >>`);
        objStream(content, "", enc.encode(`q ${f(p.w)} 0 0 ${f(p.h)} 0 0 cm /Im0 Do Q`));
        objStream(img, `/Type /XObject /Subtype /Image /Width ${p.px} /Height ${p.py} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode`, p.jpeg);
    });
    const total = 3 + pages.length * 3;
    const xref = len;
    put(`xref\n0 ${total}\n0000000000 65535 f \n`);
    for (let n = 1; n < total; n++) put(String(offsets[n]).padStart(10, "0") + " 00000 n \n");
    put(`trailer\n<< /Size ${total} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`);
    const out = new Uint8Array(len);
    let o = 0;
    for (const c of chunks) { out.set(c, o); o += c.length; }
    return out;
}

let lastFile = null;

async function finishDelivery(share) {
    const file = lastFile;
    if (share && navigator.canShare && navigator.canShare({ files: [file] })) {
        try { await navigator.share({ files: [file], title: file.name }); return "shared"; }
        catch (e) {
            if (e && e.name === "AbortError") return "cancelled";
            if (e && e.name === "NotAllowedError") return "needs-gesture";
            throw e;
        }
    }
    const url = URL.createObjectURL(file);
    const a = document.createElement("a");
    a.href = url; a.download = file.name;
    document.body.appendChild(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60000);
    return "downloaded";
}

function deliver(bytes, fileName, share) {
    lastFile = new File([bytes], fileName, { type: "application/pdf" });
    return finishDelivery(share);
}

export function exportFinish(fileName, share) {
    const bytes = buildPdf(exp.pages);
    exp = null;
    return deliver(bytes, fileName, share);
}

export async function exportOriginal(name, fileName, share) {
    const bytes = await readFile("sheets/" + name);
    if (!bytes) throw new Error("PDF file missing from browser storage.");
    return deliver(bytes, fileName, share);
}

export function shareLast() { return finishDelivery(true); }

export function paintTilesJson(container, json) { return paintTiles(container, JSON.parse(json)); }
