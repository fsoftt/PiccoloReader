// Web Audio plumbing for the metronome, the piano and the tuner.
//
// Output: .NET renders PCM blocks (MeltySynth, metronome clicks) and pushes them here; an
// AudioWorklet "sink" plays them from a queue and reports how many frames it has played and still
// holds, so .NET keeps a short, constant lead and places every click by absolute frame index
// (sample accurate, no timers involved in the timing itself).
// Input: getUserMedia -> AudioWorklet "capture" -> blocks of float32 samples back to .NET.

const WORKLET = `
class Sink extends AudioWorkletProcessor {
    constructor(options) {
        super();
        this.channels = (options.processorOptions && options.processorOptions.channels) || 2;
        this.queue = [];      // {data: Float32Array (planar: L then R), frames, pos}
        this.queued = 0;
        this.played = 0;
        this.tick = 0;
        this.port.onmessage = (e) => {
            const m = e.data;
            if (m.flush) { this.queue.length = 0; this.queued = 0; return; }
            if (m.pcm) { this.queue.push({ data: m.pcm, frames: m.frames, pos: 0 }); this.queued += m.frames; }
        };
    }
    process(inputs, outputs) {
        const out = outputs[0];
        const n = out[0].length;
        let done = 0;
        while (done < n && this.queue.length) {
            const c = this.queue[0];
            const take = Math.min(n - done, c.frames - c.pos);
            out[0].set(c.data.subarray(c.pos, c.pos + take), done);
            if (out.length > 1) {
                const off = this.channels > 1 ? c.frames : 0;
                out[1].set(c.data.subarray(off + c.pos, off + c.pos + take), done);
            }
            c.pos += take; done += take; this.queued -= take;
            if (c.pos >= c.frames) this.queue.shift();
        }
        if (done < n && this.started) this.under = (this.under || 0) + 1;
        if (done > 0) this.started = true;
        this.played += done;
        if ((this.tick++ & 3) === 0) this.port.postMessage({ played: this.played, queued: this.queued, under: this.under || 0 });
        return true;
    }
}
registerProcessor("sink", Sink);

class Capture extends AudioWorkletProcessor {
    constructor() { super(); this.buf = new Float32Array(2048); this.n = 0; }
    process(inputs) {
        const ch = inputs[0] && inputs[0][0];
        if (ch) {
            let i = 0;
            while (i < ch.length) {
                const take = Math.min(ch.length - i, this.buf.length - this.n);
                this.buf.set(ch.subarray(i, i + take), this.n);
                this.n += take; i += take;
                if (this.n === this.buf.length) {
                    this.port.postMessage(this.buf, [this.buf.buffer]);
                    this.buf = new Float32Array(2048); this.n = 0;
                }
            }
        }
        return true;
    }
}
registerProcessor("capture", Capture);
`;

// Diagnostics read by tests and by hand from the console (window.__audioStats).
const stats = { reports: 0, underruns: {}, keyToQueueMs: [], lastDown: 0, outputLatencyMs: 0 };
self.__audioStats = stats;

let ctx = null;
let ready = null;
let host = null;           // DotNetObjectReference of WebAudio
const streams = new Map(); // name -> { node, channels }
let mic = null;            // { stream, source, node, mute }
let micState = "unknown";  // unknown | prompt | granted | denied | unsupported

export function supported() {
    return typeof AudioContext !== "undefined" && typeof AudioWorkletNode !== "undefined";
}

// Creates the context and loads the worklet once. Safe to call repeatedly.
export function init(dotnet) {
    host = dotnet;
    if (!ready) {
        ready = (async () => {
            if (!supported()) throw new Error("Web Audio worklets are not available");
            ctx = new AudioContext({ latencyHint: "interactive" });
            const url = URL.createObjectURL(new Blob([WORKLET], { type: "text/javascript" }));
            await ctx.audioWorklet.addModule(url);
            URL.revokeObjectURL(url);
            // iOS Safari and autoplay policies only start audio from a user gesture.
            const unlock = () => { resume(); };
            for (const ev of ["pointerdown", "touchend", "keydown", "click"]) {
                document.addEventListener(ev, unlock, { capture: true, passive: true });
            }
            ctx.onstatechange = () => { };
            return true;
        })();
        ready.catch(() => { ready = null; });
    }
    return ready;
}

export function sampleRate() { return ctx ? ctx.sampleRate : 48000; }
export function state() { return ctx ? ctx.state : "none"; }
export function latencySeconds() { return ctx ? (ctx.baseLatency || 0) + (ctx.outputLatency || 0) : 0; }

export async function resume() {
    if (ctx && ctx.state !== "running") {
        try { await ctx.resume(); } catch { /* needs a gesture; the next one retries */ }
    }
    return ctx ? ctx.state : "none";
}

// ---- output streams -------------------------------------------------------------------------

export function streamStart(name, channels) {
    streamStop(name);
    const node = new AudioWorkletNode(ctx, "sink", {
        numberOfInputs: 0, numberOfOutputs: 1, outputChannelCount: [2],
        processorOptions: { channels }
    });
    node.port.onmessage = (e) => { stats.underruns[name] = e.data.under; stats.reports++; host.invokeMethod("OnStreamReport", name, e.data.played, e.data.queued); };
    node.connect(ctx.destination);
    streams.set(name, { node, channels });
    resume();
}

// pcm: planar float32 bytes (left block, then right block unless mono).
export function streamPush(name, bytes, frames) {
    const s = streams.get(name);
    if (!s) return;
    const copy = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
    const pcm = new Float32Array(copy);
    if (stats.lastDown) {
        for (let i = 0; i < frames; i++) if (Math.abs(pcm[i]) > 0.001) { stats.keyToQueueMs.push(performance.now() - stats.lastDown); stats.lastDown = 0; break; }
    }
    stats.outputLatencyMs = ctx ? Math.round(((ctx.baseLatency || 0) + (ctx.outputLatency || 0)) * 1000) : 0;
    s.node.port.postMessage({ pcm, frames }, [copy]);
}

export function streamFlush(name) {
    const s = streams.get(name);
    if (s) s.node.port.postMessage({ flush: true });
}

export function streamStop(name) {
    const s = streams.get(name);
    if (!s) return;
    s.node.port.onmessage = null;
    try { s.node.disconnect(); } catch { }
    streams.delete(name);
}

// ---- sound font -----------------------------------------------------------------------------

// Cache API first (the file is 7.9 MB and never changes between releases of the same URL),
// then the network. Resolves to a Uint8Array.
export async function fetchCached(url) {
    try {
        if (self.caches) {
            const cache = await caches.open("piccolo-sf2-v1");
            let res = await cache.match(url);
            if (!res) {
                res = await fetch(url);
                if (!res.ok) throw new Error("HTTP " + res.status);
                await cache.put(url, res.clone());
            }
            return new Uint8Array(await res.arrayBuffer());
        }
    } catch (e) {
        if (!(e && e.name === "QuotaExceededError")) {
            // fall through to a plain fetch for any cache failure
        }
    }
    const res = await fetch(url);
    if (!res.ok) throw new Error("HTTP " + res.status);
    return new Uint8Array(await res.arrayBuffer());
}

// ---- microphone -----------------------------------------------------------------------------

export function micPermission() { return micState; }

export async function micRefresh() {
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) { micState = "unsupported"; return micState; }
    try {
        const r = await navigator.permissions.query({ name: "microphone" });
        micState = r.state;
        r.onchange = () => { micState = r.state; };
    } catch { /* Safari / Firefox: unknown until asked */ }
    return micState;
}

// Opens the microphone (shows the browser prompt). Returns granted | denied | unsupported | error.
export async function micRequest() {
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) { micState = "unsupported"; return "unsupported"; }
    if (mic && mic.stream.active) return "granted";
    try {
        // No echo cancellation, noise suppression or auto gain: they would distort the pitch.
        const stream = await navigator.mediaDevices.getUserMedia({
            audio: { echoCancellation: false, noiseSuppression: false, autoGainControl: false, channelCount: 1 }
        });
        mic = { stream, source: null, node: null, mute: null };
        micState = "granted";
        return "granted";
    } catch (e) {
        if (e && (e.name === "NotAllowedError" || e.name === "SecurityError")) { micState = "denied"; return "denied"; }
        if (e && e.name === "NotFoundError") return "error";
        return "error";
    }
}

// Connects the opened microphone to the capture worklet. Returns the sample rate, or 0.
export function micStart() {
    if (!mic || !ctx) return 0;
    micStop(false);
    mic.source = ctx.createMediaStreamSource(mic.stream);
    mic.node = new AudioWorkletNode(ctx, "capture", { numberOfInputs: 1, numberOfOutputs: 1, outputChannelCount: [1] });
    mic.node.port.onmessage = (e) => {
        const f = e.data;
        host.invokeMethod("OnMicBlock", new Uint8Array(f.buffer, f.byteOffset, f.byteLength));
    };
    // The worklet must be pulled by the graph; a muted gain keeps it alive without feedback.
    mic.mute = ctx.createGain();
    mic.mute.gain.value = 0;
    mic.source.connect(mic.node);
    mic.node.connect(mic.mute);
    mic.mute.connect(ctx.destination);
    resume();
    return ctx.sampleRate;
}

// Releases the microphone completely (also removes the browser's recording indicator).
export function micStop(release = true) {
    if (!mic) return;
    try { mic.source && mic.source.disconnect(); } catch { }
    try { mic.node && (mic.node.port.onmessage = null, mic.node.disconnect()); } catch { }
    try { mic.mute && mic.mute.disconnect(); } catch { }
    mic.source = mic.node = mic.mute = null;
    if (release) {
        mic.stream.getTracks().forEach(t => t.stop());
        mic = null;
    }
}

export function micActive() { return !!(mic && mic.stream.active && mic.stream.getAudioTracks().some(t => t.readyState === "live")); }

// ---- piano keyboard (Pointer Events, multi-touch, glissando) --------------------------------

const boards = new WeakMap();

function noteAt(x, y) {
    const el = document.elementFromPoint(x, y);
    const key = el && el.closest ? el.closest("[data-note]") : null;
    return key && key.dataset.note ? key : null;
}

function velocityOf(key, y) {
    const r = key.getBoundingClientRect();
    const f = r.height > 0 ? Math.min(1, Math.max(0, (y - r.top) / r.height)) : 0;
    return 70 + Math.round(50 * f);
}

// root: the keyboard container; dotnet: object with NoteOn(note, velocity), NoteOff(note).
export function pianoAttach(root, dotnet) {
    pianoDetach(root);
    const pointers = new Map();   // pointerId -> note
    const counts = new Map();     // note -> pointers holding it
    const minimap = document.querySelector("[data-minimap]");
    const mark = (note, on) => {
        for (const el of document.querySelectorAll('[data-note="' + note + '"],[data-mm="' + note + '"]')) el.classList.toggle("down", on);
    };
    const press = (note, vel) => {
        const c = (counts.get(note) || 0) + 1;
        counts.set(note, c);
        if (c === 1) { mark(note, true); dotnet.invokeMethod("NoteOn", note, vel); }
    };
    const lift = (note) => {
        const c = (counts.get(note) || 0) - 1;
        if (c > 0) { counts.set(note, c); return; }
        counts.delete(note);
        mark(note, false);
        dotnet.invokeMethod("NoteOff", note);
    };
    const moveTo = (id, x, y) => {
        const key = noteAt(x, y);
        const target = key && root.contains(key) ? +key.dataset.note : -1;
        const cur = pointers.has(id) ? pointers.get(id) : -1;
        if (cur === target) return;
        if (cur >= 0) lift(cur);
        pointers.set(id, target);
        if (target >= 0) press(target, velocityOf(key, y));
    };
    const end = (e) => {
        if (!pointers.has(e.pointerId)) return;
        const cur = pointers.get(e.pointerId);
        pointers.delete(e.pointerId);
        if (cur >= 0) lift(cur);
    };
    const down = (e) => {
        if (e.pointerType === "mouse" && e.button !== 0) return;
        e.preventDefault();
        try { root.setPointerCapture(e.pointerId); } catch { }
        resume();
        stats.lastDown = performance.now();
        pointers.set(e.pointerId, -1);
        moveTo(e.pointerId, e.clientX, e.clientY);
    };
    const move = (e) => { if (pointers.has(e.pointerId)) moveTo(e.pointerId, e.clientX, e.clientY); };
    const blockMenu = (e) => e.preventDefault();
    root.addEventListener("pointerdown", down);
    root.addEventListener("pointermove", move);
    root.addEventListener("pointerup", end);
    root.addEventListener("pointercancel", end);
    root.addEventListener("lostpointercapture", end);
    root.addEventListener("contextmenu", blockMenu);
    boards.set(root, {
        release() {
            for (const cur of pointers.values()) if (cur >= 0) lift(cur);
            pointers.clear(); counts.clear();
        },
        off() {
            root.removeEventListener("pointerdown", down);
            root.removeEventListener("pointermove", move);
            root.removeEventListener("pointerup", end);
            root.removeEventListener("pointercancel", end);
            root.removeEventListener("lostpointercapture", end);
            root.removeEventListener("contextmenu", blockMenu);
        }
    });
}

export function pianoRelease(root) { const b = boards.get(root); if (b) b.release(); }

export function pianoDetach(root) {
    const b = boards.get(root);
    if (b) { b.release(); b.off(); boards.delete(root); }
}

// Mini-map: drag the window or tap to centre it; reports the pointer x as a 0..1 fraction.
export function minimapAttach(root, dotnet) {
    let dragging = false;
    const rect = () => root.getBoundingClientRect();
    const frac = (e) => { const r = rect(); return Math.min(1, Math.max(0, (e.clientX - r.left) / r.width)); };
    root.onpointerdown = (e) => {
        dragging = true; root.setPointerCapture(e.pointerId); e.preventDefault();
        dotnet.invokeMethod("MiniMapPress", frac(e));
    };
    root.onpointermove = (e) => { if (dragging) dotnet.invokeMethod("MiniMapMove", frac(e)); };
    root.onpointerup = root.onpointercancel = () => { dragging = false; };
}

// Reports portrait/landscape changes (the piano rearranges itself like the Android page).
let orientationOff = null;
export function watchOrientation(dotnet) {
    unwatchOrientation();
    const mq = matchMedia("(orientation: landscape)");
    const send = () => dotnet.invokeMethod("OrientationChanged", mq.matches);
    mq.addEventListener("change", send);
    orientationOff = () => mq.removeEventListener("change", send);
    send();
}
export function unwatchOrientation() { if (orientationOff) { orientationOff(); orientationOff = null; } }
