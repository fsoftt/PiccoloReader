// Drag-to-reorder for sheet rows (the Android app's ItemTouchHelper behavior):
// the handle starts the drag (long press on touch, immediately for a mouse), the
// dragged row follows the pointer at 85% opacity, the other rows slide out of
// the way, the list auto-scrolls near its edges, and the drop reports the move.
let ref = null;
let drag = null;
const LONG_PRESS_MS = 350;

export function init(dotnetRef) {
    dispose();
    ref = dotnetRef;
    document.addEventListener("pointerdown", onDown, true);
}

export function dispose() {
    document.removeEventListener("pointerdown", onDown, true);
    cancel();
    ref = null;
}

function scroller(el) {
    for (let p = el.parentElement; p; p = p.parentElement) {
        const o = getComputedStyle(p).overflowY;
        if ((o === "auto" || o === "scroll") && p.scrollHeight > p.clientHeight) return p;
    }
    return document.scrollingElement;
}

function onDown(e) {
    const handle = e.target.closest?.(".reorder");
    if (!handle || (e.pointerType === "mouse" && e.button !== 0)) return;
    const row = handle.closest(".sheet-row");
    if (!row) return;
    e.preventDefault();
    cancel();
    const d = drag = { row, id: e.pointerId, x: e.clientX, y: e.clientY, started: false, handle };
    const begin = () => { if (drag === d) start(d); };
    if (e.pointerType === "mouse") begin();
    else d.timer = setTimeout(begin, LONG_PRESS_MS);
    d.move = (ev) => {
        if (ev.pointerId !== d.id) return;
        d.cx = ev.clientX; d.cy = ev.clientY;
        if (!d.started) {
            // Moving before the long press fires means the user is scrolling.
            if (Math.hypot(ev.clientX - d.x, ev.clientY - d.y) > 10) cancel();
            return;
        }
        ev.preventDefault();
        update(d);
    };
    d.up = (ev) => { if (ev.pointerId === d.id) finish(d, ev.type === "pointerup"); };
    window.addEventListener("pointermove", d.move, { passive: false });
    window.addEventListener("pointerup", d.up);
    window.addEventListener("pointercancel", d.up);
    d.block = (ev) => { if (d.started) ev.preventDefault(); };
    window.addEventListener("touchmove", d.block, { passive: false });
    d.cx = e.clientX; d.cy = e.clientY;
}

function start(d) {
    d.started = true;
    d.rows = [...d.row.parentElement.querySelectorAll(":scope > .sheet-row")];
    d.from = d.rows.indexOf(d.row);
    d.to = d.from;
    d.scroller = scroller(d.row);
    d.startScroll = d.scroller.scrollTop;
    d.rects = d.rows.map(r => r.getBoundingClientRect());
    try { d.handle.setPointerCapture(d.id); } catch { }
    d.row.classList.add("dragging");
    for (const r of d.rows) if (r !== d.row) r.classList.add("reorder-shift");
    if (navigator.vibrate) navigator.vibrate(10);
    d.raf = requestAnimationFrame(() => tick(d));
}

function update(d) {
    const dy = d.cy - d.y + (d.scroller.scrollTop - d.startScroll);
    d.row.style.transform = "translateY(" + dy + "px)";
    const home = d.rects[d.from];
    const center = home.top + home.height / 2 + dy; // in start-scroll coordinates
    let to = d.from;
    for (let i = 0; i < d.rects.length; i++) {
        const mid = d.rects[i].top + d.rects[i].height / 2;
        if (i < d.from && center < mid) { to = i; break; }
        if (i > d.from && center > mid) to = i;
    }
    d.to = to;
    const h = home.height;
    d.rows.forEach((r, i) => {
        if (r === d.row) return;
        let s = 0;
        if (d.from < to && i > d.from && i <= to) s = -h;
        else if (d.from > to && i >= to && i < d.from) s = h;
        r.style.transform = s ? "translateY(" + s + "px)" : "";
    });
}

// Auto-scroll while the pointer is near the top/bottom edge of the list.
function tick(d) {
    if (drag !== d) return;
    const sc = d.scroller;
    const r = sc === document.scrollingElement ? { top: 0, bottom: window.innerHeight } : sc.getBoundingClientRect();
    const zone = 64;
    let v = 0;
    if (d.cy < r.top + zone) v = -Math.ceil((r.top + zone - d.cy) / 4);
    else if (d.cy > r.bottom - zone) v = Math.ceil((d.cy - (r.bottom - zone)) / 4);
    if (v) { sc.scrollTop += v; update(d); }
    d.raf = requestAnimationFrame(() => tick(d));
}

function finish(d, ok) {
    const from = d.from, to = d.to, started = d.started;
    cancel();
    if (ok && started && from !== to && ref) ref.invokeMethodAsync("OnSheetReordered", from, to);
}

function cancel() {
    const d = drag;
    if (!d) return;
    drag = null;
    clearTimeout(d.timer);
    cancelAnimationFrame(d.raf);
    window.removeEventListener("pointermove", d.move);
    window.removeEventListener("pointerup", d.up);
    window.removeEventListener("pointercancel", d.up);
    window.removeEventListener("touchmove", d.block);
    try { d.handle.releasePointerCapture(d.id); } catch { }
    if (d.rows) for (const r of d.rows) { r.style.transform = ""; r.classList.remove("dragging", "reorder-shift"); }
}
