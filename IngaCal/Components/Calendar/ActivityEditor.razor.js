const DISMISS = "OnDismiss";
const dialogs = new WeakMap();
export function open(element, reference) {
    const abort = new AbortController(), previous = document.activeElement;
    dialogs.set(element, { abort, previous });
    element.addEventListener("cancel", async event => {
        event.preventDefault();
        try { await reference.invokeMethodAsync(DISMISS); } catch { }
    }, { signal: abort.signal });
    element.showModal();
    element.querySelector("#activity-title")?.focus();
}
export function close(element) {
    const state = dialogs.get(element);
    state?.abort.abort();
    if (element.open) element.close();
    if (state?.previous?.isConnected) state.previous.focus();
    dialogs.delete(element);
}
