export function subscribe(dotNetReference) {
    const source = new EventSource("api/runs/events");
    let latest = null;
    let scheduled = false;
    let dispatching = false;
    const dispatch = async () => {
        scheduled = false;
        if (dispatching || latest === null) return;
        dispatching = true;
        const snapshot = latest;
        latest = null;
        try { await dotNetReference.invokeMethodAsync("OnRunSnapshot", snapshot); }
        finally {
            dispatching = false;
            if (latest !== null && !scheduled) { scheduled = true; requestAnimationFrame(dispatch); }
        }
    };
    source.addEventListener("snapshot", event => {
        latest = event.data;
        if (!scheduled && !dispatching) { scheduled = true; requestAnimationFrame(dispatch); }
    });
    return { dispose: () => source.close() };
}

export function isFocused() { return document.visibilityState === "visible" && document.hasFocus(); }

export function watchFocus(dotNetReference) {
    const handler = () => dotNetReference.invokeMethodAsync("OnApplicationFocusChanged", isFocused());
    window.addEventListener("focus", handler); window.addEventListener("blur", handler); document.addEventListener("visibilitychange", handler);
    return { dispose: () => { window.removeEventListener("focus", handler); window.removeEventListener("blur", handler); document.removeEventListener("visibilitychange", handler); } };
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

export async function enableNotifications() { const permission = await Notification.requestPermission(); localStorage.setItem("ai-client.notifications", permission); return permission; }
export function notificationsEnabled() { return localStorage.getItem("ai-client.notifications") === "granted" && Notification.permission === "granted"; }
export function notify(title, body) { if (notificationsEnabled() && !isFocused()) new Notification(title, { body }); }
