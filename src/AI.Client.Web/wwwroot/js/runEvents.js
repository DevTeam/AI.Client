// The frontend runs in its own process on a separate origin from the API, so a relative
// URL like "api/runs/events" used to work only when both were served from the same origin.
// Now we expect an absolute base URL (e.g. `http://localhost:52173/`) and append the API path
// ourselves. Trailing slashes on the base are tolerated.
export function subscribe(baseUrl, dotNetReference) {
    const trimmed = (baseUrl || "").replace(/\/+$/, "");
    const url = trimmed + "/api/runs/events";
    const source = new EventSource(url);
    let latest = null;
    let dispatching = false;
    let disposed = false;
    const dispatch = async () => {
        if (dispatching || disposed) return;
        dispatching = true;
        try {
            while (latest !== null && !disposed) {
                const snapshot = latest;
                latest = null;
                await dotNetReference.invokeMethodAsync("OnRunSnapshot", snapshot);
            }
        } catch (error) {
            if (!disposed) console.error("Run snapshot could not be applied", error);
        } finally {
            dispatching = false;
        }
    };
    source.addEventListener("snapshot", event => {
        latest = event.data;
        void dispatch();
    });
    // The Host says only that something changed, never what. One pending reload is enough however
    // many signals arrive while it runs, so they collapse into a flag rather than a queue.
    let reloadPending = false;
    let reloading = false;
    const reload = async () => {
        if (reloading || disposed) return;
        reloading = true;
        try {
            while (reloadPending && !disposed) {
                reloadPending = false;
                await dotNetReference.invokeMethodAsync("OnApplicationDataChanged");
            }
        } catch (error) {
            if (!disposed) console.error("Application data could not be reloaded", error);
        } finally {
            reloading = false;
        }
    };
    source.addEventListener("data-changed", () => {
        reloadPending = true;
        void reload();
    });
    return { dispose: () => { disposed = true; latest = null; reloadPending = false; source.close(); } };
}
export function isFocused() { return document.visibilityState === "visible" && document.hasFocus(); }

export function watchFocus(dotNetReference) {
    const handler = () => dotNetReference.invokeMethodAsync("OnApplicationFocusChanged", isFocused());
    window.addEventListener("focus", handler); window.addEventListener("blur", handler); document.addEventListener("visibilitychange", handler);
    return { dispose: () => { window.removeEventListener("focus", handler); window.removeEventListener("blur", handler); document.removeEventListener("visibilitychange", handler); } };
}

export function watchEscape(dotNetReference) {
    const locallyHandled = ".sidebar-search-input, .sidebar-inline-editor, .queue-item input, .project-settings-modal, .message-branch-indicator";
    const handler = event => {
        if (event.key !== "Escape" || event.repeat || event.defaultPrevented) return;
        const target = event.target instanceof Element ? event.target : null;
        if (target?.closest(locallyHandled)) return;
        void dotNetReference.invokeMethodAsync("OnEscapePressed");
    };
    document.addEventListener("keydown", handler);
    return { dispose: () => document.removeEventListener("keydown", handler) };
}

export function watchQueueDrag(dotNetReference) {
    let source = null;
    let target = null;
    const clear = () => {
        document.querySelectorAll(".queue-item.dragging, .queue-item.drop-target").forEach(item => item.classList.remove("dragging", "drop-target"));
        source = null; target = null;
    };
    const down = event => {
        const handle = event.target instanceof Element ? event.target.closest(".queue-drag-handle") : null;
        const item = handle?.closest(".queue-item[data-queue-id]");
        if (!item || event.button !== 0) return;
        event.preventDefault();
        source = item;
        target = item;
        source.classList.add("dragging");
        handle.setPointerCapture?.(event.pointerId);
    };
    const move = event => {
        if (!source) return;
        event.preventDefault();
        const item = document.elementFromPoint(event.clientX, event.clientY)?.closest(".queue-item[data-queue-id]");
        if (!item) return;
        target?.classList.remove("drop-target");
        target = item;
        if (target !== source) target.classList.add("drop-target");
    };
    const up = event => {
        if (!source) return;
        event.preventDefault();
        const sourceId = source.dataset.queueId;
        const targetId = target?.dataset.queueId;
        clear();
        if (sourceId && targetId && sourceId !== targetId) dotNetReference.invokeMethodAsync("DropQueuedMessageAsync", sourceId, targetId);
    };
    document.addEventListener("pointerdown", down); document.addEventListener("pointermove", move); document.addEventListener("pointerup", up); document.addEventListener("pointercancel", clear);
    return { dispose: () => { document.removeEventListener("pointerdown", down); document.removeEventListener("pointermove", move); document.removeEventListener("pointerup", up); document.removeEventListener("pointercancel", clear); } };
}

// Compatibility for clients that were already open while the notifications UI was removed.
// These exports intentionally do nothing and can be deleted after old tabs can no longer exist.
export async function enableNotifications() { return "denied"; }
export function notificationsEnabled() { return false; }
export function notify() {}
