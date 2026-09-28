// The frontend runs in its own process on a separate origin from the API, so a relative
// URL like "api/runs/events" used to work only when both were served from the same origin.
// Now we expect an absolute base URL (e.g. `http://localhost:52173/`) and append the API path
// ourselves. Trailing slashes on the base are tolerated.
export function subscribe(baseUrl, dotNetReference) {
    const trimmed = (baseUrl || "").replace(/\/+$/, "");
    const url = trimmed + "/api/runs/events";
    const controller = new AbortController();
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
    const onSnapshot = data => { latest = data; void dispatch(); };
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
    const onDataChanged = () => {
        reloadPending = true;
        void reload();
    };
    // fetch can send the browser grant in a header; EventSource cannot. The same parser is used
    // for the local UI, the development server, and the published website.
    const readEvents = async () => {
        while (!disposed) {
            try {
                const token = window.aiHostBridge?.token();
                const response = await fetch(url, {
                    headers: token ? { Authorization: `Bearer ${token}` } : {},
                    signal: controller.signal,
                    cache: "no-store"
                });
                if (!response.ok || !response.body) throw new Error(`Events returned ${response.status}`);
                const reader = response.body.getReader();
                const decoder = new TextDecoder();
                let pending = "";
                while (!disposed) {
                    const { value, done } = await reader.read();
                    if (done) break;
                    pending = (pending + decoder.decode(value, { stream: true })).replace(/\r\n/g, "\n");
                    let end;
                    while ((end = pending.indexOf("\n\n")) >= 0) {
                        const frame = pending.slice(0, end);
                        pending = pending.slice(end + 2);
                        const event = frame.split("\n").find(line => line.startsWith("event: "))?.slice(7);
                        const data = frame.split("\n").filter(line => line.startsWith("data: ")).map(line => line.slice(6)).join("\n");
                        if (event === "snapshot") onSnapshot(data);
                        else if (event === "data-changed") onDataChanged();
                    }
                }
            } catch (error) {
                if (!disposed) console.warn("Run events disconnected", error);
            }
            if (!disposed) await new Promise(resolve => setTimeout(resolve, 2000));
        }
    };
    void readEvents();
    return { dispose: () => { disposed = true; latest = null; reloadPending = false; controller.abort(); } };
}
export function isFocused() { return document.visibilityState === "visible" && document.hasFocus(); }

export function watchFocus(dotNetReference) {
    const handler = () => dotNetReference.invokeMethodAsync("OnApplicationFocusChanged", isFocused());
    window.addEventListener("focus", handler); window.addEventListener("blur", handler); document.addEventListener("visibilitychange", handler);
    return { dispose: () => { window.removeEventListener("focus", handler); window.removeEventListener("blur", handler); document.removeEventListener("visibilitychange", handler); } };
}

export function watchEscape(dotNetReference) {
    const locallyHandled = ".sidebar-search-input, .sidebar-inline-editor, .queue-item input, .message-branch-indicator, .message-review-editor, .message-review-toggle, .message-review-comments, .review-comment-editor";
    const handler = event => {
        if (event.key !== "Escape" || event.repeat || event.defaultPrevented) return;
        const target = event.target instanceof Element ? event.target : null;
        if (target?.closest(locallyHandled)) return;
        // Settings drawers save on close, and a field commits its value on change, which only
        // fires on blur: blur first so the last edit is bound before the save reads it.
        if (target?.closest(".permissions-drawer") && document.activeElement instanceof HTMLElement) document.activeElement.blur();
        void dotNetReference.invokeMethodAsync("OnEscapePressed");
    };
    document.addEventListener("keydown", handler);
    return { dispose: () => document.removeEventListener("keydown", handler) };
}

export function showReviewSubmenu(triggerId, popupId) {
    const trigger = document.getElementById(triggerId);
    const popup = document.getElementById(popupId);
    if (!trigger || !popup) return;
    if (!popup.matches(":popover-open")) popup.showPopover();
    const anchor = trigger.getBoundingClientRect();
    const width = popup.getBoundingClientRect().width;
    const height = popup.getBoundingClientRect().height;
    const gap = 6;
    let left = anchor.right + gap;
    if (left + width + gap > window.innerWidth) left = anchor.left - width - gap;
    popup.style.left = `${Math.max(gap, Math.min(left, window.innerWidth - width - gap))}px`;
    popup.style.top = `${Math.max(gap, Math.min(anchor.top, window.innerHeight - height - gap))}px`;
    popup.querySelector("button")?.focus();
}

export function hideReviewSubmenus() {
    document.querySelectorAll(".review-submenu:popover-open").forEach(popup => popup.hidePopover());
}

/**
 * Closes a right-hand drawer or review on a press anywhere outside it without swallowing that press: the
 * backdrop no longer catches clicks, so one click both closes the drawer and opens the chat (or
 * whatever else) under the pointer. Listening in the capture phase lets the close start before
 * the target's own handler runs. The Connections/MCP buttons are left to their own handler.
 */
export function watchDrawerDismiss(dotNetReference) {
    const handler = event => {
        if (event.button !== 0) return;
        const drawer = document.querySelector(".permissions-drawer, .review-workspace");
        const target = event.target instanceof Element ? event.target : null;
        if (!drawer || !target || target.closest(".permissions-drawer, .review-workspace, .sidebar-global-nav, .toast-region, .history-notifications-button")) return;
        // Same reason as on Escape: commit the focused field before the settings drawer saves.
        if (document.activeElement instanceof HTMLElement && drawer.contains(document.activeElement)) document.activeElement.blur();
        void dotNetReference.invokeMethodAsync("OnDrawerDismissed");
    };
    document.addEventListener("pointerdown", handler, true);
    return { dispose: () => document.removeEventListener("pointerdown", handler, true) };
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
