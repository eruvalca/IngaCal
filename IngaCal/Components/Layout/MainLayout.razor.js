import { applyTheme } from "../App.razor.js";
const BROWSER_CHANGED = "BrowserChanged";
let monitor;
class BrowserMonitor {
    #reference; #abort = new AbortController(); #last = "";
    constructor(reference) { this.#reference = reference; }
    read() {
        let override = "";
        try { override = sessionStorage.getItem("ingacal.timezone") || ""; } catch { }
        let detected = "";
        try { detected = Intl.DateTimeFormat().resolvedOptions().timeZone || ""; } catch { }
        return { timeZoneId: override || detected, theme: document.documentElement.dataset.bsTheme || "light" };
    }
    start() {
        const signal = this.#abort.signal;
        window.addEventListener("focus", () => this.update(), { signal });
        document.addEventListener("visibilitychange", () => { if (!document.hidden) this.update(); }, { signal });
        window.addEventListener("ingacal:theme", () => this.update(), { signal });
        applyTheme();
        this.#last = JSON.stringify(this.read());
        return this.read();
    }
    async update() {
        const state = this.read(), key = JSON.stringify(state);
        if (key === this.#last) return;
        this.#last = key;
        try { await this.#reference.invokeMethodAsync(BROWSER_CHANGED, state); } catch { }
    }
    dispose() { this.#abort.abort(); }
}
export function initialize(reference) { monitor?.dispose(); monitor = new BrowserMonitor(reference); return monitor.start(); }
export function setZone(id) {
    try { if (id) sessionStorage.setItem("ingacal.timezone", id); else sessionStorage.removeItem("ingacal.timezone"); } catch { }
    return monitor.read();
}
export function dispose() { monitor?.dispose(); monitor = null; }
