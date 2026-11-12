import { renderPage, getPageSizes } from "./pdf.js";
import { layerRendered, layerCleared, owns, rejectTouch } from "./annot.js";

const sizeCache = new Map();
async function sizesFor(name) {
    let s = sizeCache.get(name);
    if (!s) { s = await getPageSizes(name); sizeCache.set(name, s); }
    return s;
}

export async function pageSizes(name) { return await sizesFor(name); }

function cropAspect(sizes, i, crop) {
    const [l, t, r, b] = crop ?? [0, 0, 1, 1];
    return (sizes[2 * i] * (r - l)) / (sizes[2 * i + 1] * (b - t));
}

// ---------------------------------------------------------------- paged
export async function renderPaged(name, i, canvas, stage, zoom, crop) {
    const sizes = await sizesFor(name);
    const aspect = cropAspect(sizes, i, crop);
    const W = stage.clientWidth, H = stage.clientHeight;
    // Like the Android app: in landscape the page spans the full width.
    const fit = W > H ? W : Math.min(W, H * aspect);
    const css = Math.max(40, fit * zoom);
    const ok = await renderPage(name, i, canvas, css, crop);
    if (ok) layerRendered(canvas.parentElement, i, crop ?? null);
    const zoomed = css > W + 1 || css / aspect > H + 1;
    stage.classList.toggle("zoomed", zoomed);
    if (!zoomed) { stage.scrollLeft = 0; stage.scrollTop = 0; }
}

// Full page fitted into a box (crop editor).
export async function renderFit(name, i, canvas, box) {
    const sizes = await sizesFor(name);
    const aspect = cropAspect(sizes, i, null);
    const pad = 40; // matches .crop-area padding
    const css = Math.max(40, Math.min(box.clientWidth - pad, (box.clientHeight - pad) * aspect));
    await renderPage(name, i, canvas, css, null);
    // The Android crop overlay shows the page's annotations too.
    const wrap = canvas.parentElement;
    if (wrap) layerRendered(wrap, i, null);
}

export function resetScroll(stage, toBottom) {
    stage.scrollTop = toBottom ? stage.scrollHeight : 0;
    stage.scrollLeft = 0;
}

// -------------------------------------------------------------- input
let st = null;
const TURN_KEYS = new Set(["ArrowRight", "ArrowLeft", "ArrowUp", "ArrowDown", "PageUp", "PageDown", " ", "Enter", "MediaTrackNext", "MediaTrackPrevious"]);

export function attach(ref, stage, mode) {
    detach();
    const s = st = { ref, stage, mode, pointers: new Map(), pinch: null, start: null, moved: false };
    s.key = (e) => {
        const t = e.target;
        if (t && (t.tagName === "INPUT" || t.tagName === "TEXTAREA" || t.isContentEditable)) return;
        if (e.ctrlKey || e.metaKey || e.altKey) return;
        if (e.key === "+" || e.key === "=") { e.preventDefault(); ref.invokeMethodAsync("OnZoomStep", 1.25); return; }
        if (e.key === "-") { e.preventDefault(); ref.invokeMethodAsync("OnZoomStep", 0.8); return; }
        if (e.key === "0") { e.preventDefault(); ref.invokeMethodAsync("OnZoomStep", 0); return; }
        if (e.key === "Escape") { ref.invokeMethodAsync("OnEscape"); return; }
        if (!TURN_KEYS.has(e.key)) return;
        // Enter on a focused button must still activate it.
        if (e.key === "Enter" && t && (t.tagName === "BUTTON" || t.tagName === "A")) return;
        e.preventDefault();
        ref.invokeMethodAsync("OnKey", e.key, e.shiftKey);
    };
    s.wheel = (e) => {
        if (!e.ctrlKey) return;
        e.preventDefault();
        // Debounced: accumulate the wheel steps and re-render once.
        s.wheelFactor = (s.wheelFactor ?? 1) * (e.deltaY < 0 ? 1.1 : 0.9);
        clearTimeout(s.wheelTimer);
        s.wheelTimer = setTimeout(() => { const f = s.wheelFactor; s.wheelFactor = 1; ref.invokeMethodAsync("OnZoomStep", f); }, 120);
    };
    const pinchTarget = () => stage.querySelector(".pagewrap, .clist");
    s.down = (e) => {
        if (rejectTouch(e)) return;
        s.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
        if (s.pointers.size === 1) {
            // Like Android's SwipeTurn: a drag turns the page only when it STARTED at the pan limit in its direction.
            s.start = { x: e.clientX, y: e.clientY, t: performance.now(), scrollLeft: stage.scrollLeft, scrollTop: stage.scrollTop,
                atRight: stage.scrollLeft + stage.clientWidth >= stage.scrollWidth - 2, atLeft: stage.scrollLeft <= 2,
                atBottom: stage.scrollTop + stage.clientHeight >= stage.scrollHeight - 2, atTop: stage.scrollTop <= 2 };
            s.moved = false;
        } else if (s.pointers.size === 2) {
            const [a, b] = [...s.pointers.values()];
            const w = pinchTarget();
            const mx = (a.x + b.x) / 2, my = (a.y + b.y) / 2;
            const r = w ? w.getBoundingClientRect() : null;
            // Content point under the fingers (fraction of the page box): it stays under them while zooming.
            s.pinch = { d0: Math.hypot(a.x - b.x, a.y - b.y), ratio: 1, mx, my,
                fx: r && r.width ? (mx - r.left) / r.width : 0.5, fy: r && r.height ? (my - r.top) / r.height : 0 };
            if (w) w.style.transformOrigin = (s.pinch.fx * 100) + "% " + (s.pinch.fy * 100) + "%";
        }
    };
    s.move = (e) => {
        const p = s.pointers.get(e.pointerId);
        if (!p) return;
        const px0 = p.x, py0 = p.y;
        p.x = e.clientX; p.y = e.clientY;
        if (s.pinch && s.pointers.size >= 2) {
            const [a, b] = [...s.pointers.values()];
            // Two-finger pan while editing (the browser's own panning is off there).
            if (stage.classList.contains("drawing") && s.pointers.size === 2) { stage.scrollLeft -= (e.clientX - px0) / 2; stage.scrollTop -= (e.clientY - py0) / 2; }
            s.pinch.ratio = Math.hypot(a.x - b.x, a.y - b.y) / Math.max(1, s.pinch.d0);
            const w = pinchTarget();
            s.pinch.mx = (a.x + b.x) / 2; s.pinch.my = (a.y + b.y) / 2;
            if (w) w.style.transform = "scale(" + s.pinch.ratio + ")";
        }
        if (s.start && Math.hypot(e.clientX - s.start.x, e.clientY - s.start.y) > 10) s.moved = true;
    };
    s.up = (e) => {
        if (!s.pointers.has(e.pointerId)) return;
        const wasPinch = s.pinch && s.pointers.size >= 2;
        s.pointers.delete(e.pointerId);
        if (owns(e.pointerId) && !wasPinch) { s.start = null; return; }
        if (wasPinch) {
            const w = pinchTarget();
            const pin = s.pinch;
            s.pinch = null; s.start = null; s.moved = true;
            if (Math.abs(pin.ratio - 1) > 0.05) {
                // Re-render at the new zoom, then put the pinched point back under the fingers' midpoint.
                ref.invokeMethodAsync("OnZoomStep", pin.ratio).then(() => {
                    const w2 = pinchTarget();
                    if (w2) {
                        w2.style.transform = "";
                        const r = w2.getBoundingClientRect();
                        stage.scrollLeft += r.left + pin.fx * r.width - pin.mx;
                        stage.scrollTop += r.top + pin.fy * r.height - pin.my;
                    }
                });
            } else if (w) w.style.transform = "";
            return;
        }
        if (s.pinch || !s.start) return;
        const start = s.start; s.start = null;
        const dx = e.clientX - start.x, dy = e.clientY - start.y;
        const rect = stage.getBoundingClientRect();
        if (!s.moved && performance.now() - start.t < 600) {
            ref.invokeMethodAsync("OnTap", (e.clientX - rect.left) / rect.width, (e.clientY - rect.top) / rect.height);
            return;
        }
        // A swipe turns the page only when the page is not being panned.
        const zoomedX = stage.scrollWidth > stage.clientWidth + 1;
        const zoomedY = stage.scrollHeight > stage.clientHeight + 1;
        if (s.mode === "h" && Math.abs(dx) > 50 && Math.abs(dx) > Math.abs(dy) * 1.5) {
            const atEdge = !zoomedX || (dx < 0 ? start.atRight : start.atLeft);
            if (atEdge) ref.invokeMethodAsync("OnSwipe", dx < 0 ? 1 : -1);
        } else if (s.mode === "v" && Math.abs(dy) > 50 && Math.abs(dy) > Math.abs(dx) * 1.5) {
            const atEdge = !zoomedY || (dy < 0 ? start.atBottom : start.atTop);
            if (atEdge) ref.invokeMethodAsync("OnSwipe", dy < 0 ? 1 : -1);
        }
    };
    s.cancel = (e) => {
        s.pointers.delete(e.pointerId);
        if (s.pointers.size === 0) { s.start = null; s.pinch = null; }
    };
    s.resize = () => ref.invokeMethodAsync("OnResize");
    window.addEventListener("keydown", s.key);
    window.addEventListener("resize", s.resize);
    stage.addEventListener("wheel", s.wheel, { passive: false });
    stage.addEventListener("pointerdown", s.down);
    stage.addEventListener("pointermove", s.move);
    stage.addEventListener("pointerup", s.up);
    stage.addEventListener("pointercancel", s.cancel);
}

export function setMode(mode) { if (st) st.mode = mode; }

export function detach() {
    disposeContinuous();
    if (!st) return;
    const s = st; st = null;
    window.removeEventListener("keydown", s.key);
    window.removeEventListener("resize", s.resize);
    s.stage.removeEventListener("wheel", s.wheel);
    s.stage.removeEventListener("pointerdown", s.down);
    s.stage.removeEventListener("pointermove", s.move);
    s.stage.removeEventListener("pointerup", s.up);
    s.stage.removeEventListener("pointercancel", s.cancel);
}

// Pedal step on a zoomed paged page: scroll by a screen (10% overlap) or
// report "edge" so the caller turns the page.
export function step(stage, dir) {
    if (stage.scrollHeight <= stage.clientHeight + 1) return "edge";
    const h = stage.clientHeight * 0.9;
    if (dir > 0 && stage.scrollTop + stage.clientHeight < stage.scrollHeight - 2) { stage.scrollBy({ top: h, behavior: "smooth" }); return "scrolled"; }
    if (dir < 0 && stage.scrollTop > 2) { stage.scrollBy({ top: -h, behavior: "smooth" }); return "scrolled"; }
    return "edge";
}

// ----------------------------------------------------------- continuous
let cont = null;

export function observeContinuous(stage, ref, name) {
    disposeContinuous();
    const items = [...stage.querySelectorAll(".cpage")];
    const c = cont = { stage, ref, items, name, last: -1, raf: 0 };
    c.io = new IntersectionObserver((entries) => {
        for (const en of entries) {
            if (en.isIntersecting) renderItem(c, en.target);
            else clearItem(en.target);
        }
    }, { root: stage, rootMargin: "100% 0px 100% 0px" });
    items.forEach(i => c.io.observe(i));
    c.onScroll = () => {
        if (c.raf) return;
        c.raf = requestAnimationFrame(() => {
            c.raf = 0;
            const mid = stage.scrollTop + stage.clientHeight / 2;
            let best = 0;
            for (let k = 0; k < items.length; k++) {
                if (items[k].offsetTop <= mid) best = k; else break;
            }
            if (best !== c.last) { c.last = best; ref.invokeMethodAsync("OnScrolledToPage", best); }
        });
    };
    stage.addEventListener("scroll", c.onScroll, { passive: true });
}

async function renderItem(c, el) {
    const i = +el.dataset.i;
    const canvas = el.querySelector("canvas");
    const width = el.clientWidth;
    if (el._w === width) return;
    el._w = width;
    const crop = el.dataset.crop ? el.dataset.crop.split(",").map(Number) : null;
    try { if (await renderPage(c.name, i, canvas, width, crop)) layerRendered(el, i, crop); }
    catch (e) { console.error(e); el._w = 0; }
}

function clearItem(el) {
    const canvas = el.querySelector("canvas");
    if (canvas && el._w) { canvas.width = 1; canvas.height = 1; el._w = 0; layerCleared(el); }
}

export function setZoom(stage, zoom) {
    stage.style.setProperty("--zoom", zoom);
    if (!cont) return;
    const s = stage.getBoundingClientRect();
    for (const el of cont.items) {
        el._w = 0;
        const r = el.getBoundingClientRect();
        if (r.bottom > s.top - s.height && r.top < s.bottom + s.height) renderItem(cont, el);
    }
}

export function scrollToPage(stage, i) {
    const el = stage.querySelectorAll(".cpage")[i];
    if (el) stage.scrollTop = el.offsetTop;
    if (cont) cont.last = i;
}

export function scrollStep(stage, dir) {
    const h = stage.clientHeight * 0.9;
    const atEnd = stage.scrollTop + stage.clientHeight >= stage.scrollHeight - 2;
    const atStart = stage.scrollTop <= 2;
    if ((dir > 0 && atEnd) || (dir < 0 && atStart)) return "edge";
    stage.scrollBy({ top: dir * h, behavior: "smooth" });
    return "scrolled";
}

export function disposeContinuous() {
    if (!cont) return;
    cont.io.disconnect();
    cont.stage.removeEventListener("scroll", cont.onScroll);
    cont = null;
}

// ------------------------------------------------------------ crop editor
let crop = null;
const HANDLES = ["TopLeft", "Top", "TopRight", "Left", "Right", "BottomLeft", "Bottom", "BottomRight"];

export function cropStart(layer, initial) {
    cropStop();
    layer.innerHTML = "";
    const box = document.createElement("div");
    box.className = "crop-box";
    layer.appendChild(box);
    const c = crop = { layer, box, rect: initial.slice(), drag: null };
    for (const h of HANDLES) {
        const el = document.createElement("div");
        el.className = "crop-handle h-" + h;
        el.dataset.h = h;
        box.appendChild(el);
    }
    const apply = () => {
        const [l, t, r, b] = c.rect;
        box.style.left = l * 100 + "%"; box.style.top = t * 100 + "%";
        box.style.width = (r - l) * 100 + "%"; box.style.height = (b - t) * 100 + "%";
    };
    c.apply = apply;
    apply();
    c.down = (e) => {
        const h = e.target.dataset?.h;
        if (!h) return;
        e.preventDefault();
        layer.setPointerCapture(e.pointerId);
        c.drag = { h, x: e.clientX, y: e.clientY, rect: c.rect.slice(), id: e.pointerId };
    };
    c.move = (e) => {
        if (!c.drag || e.pointerId !== c.drag.id) return;
        const r = layer.getBoundingClientRect();
        const dx = (e.clientX - c.drag.x) / r.width, dy = (e.clientY - c.drag.y) / r.height;
        const [l, t, rr, b] = c.drag.rect;
        c.rect = DotNet.invokeMethod("PiccoloReader.Web", "DragCrop", c.drag.h, l, t, rr, b, dx, dy);
        apply();
    };
    c.up = (e) => { if (c.drag && e.pointerId === c.drag.id) c.drag = null; };
    layer.addEventListener("pointerdown", c.down);
    layer.addEventListener("pointermove", c.move);
    layer.addEventListener("pointerup", c.up);
    layer.addEventListener("pointercancel", c.up);
}

export function cropGet() { return crop ? crop.rect : null; }

export function cropReset() {
    if (!crop) return;
    crop.rect = [0, 0, 1, 1];
    crop.apply();
}

export function cropStop() {
    if (!crop) return;
    const c = crop; crop = null;
    c.layer.removeEventListener("pointerdown", c.down);
    c.layer.removeEventListener("pointermove", c.move);
    c.layer.removeEventListener("pointerup", c.up);
    c.layer.removeEventListener("pointercancel", c.up);
    c.layer.innerHTML = "";
    const wrap = c.layer.parentElement;
    if (wrap) layerCleared(wrap);
}
