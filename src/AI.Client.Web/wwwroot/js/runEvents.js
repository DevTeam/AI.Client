export function subscribe(dotNetReference) {
    const source = new EventSource("api/runs/events");
    // FIFO queue, not a single "latest wins" slot: each event already carries a full snapshot of
    // every run, but Home.razor's OnRunSnapshot depends on seeing the SPECIFIC edge transition
    // (Generating -> non-Generating) for each queued message to know it's safe to refetch the
    // chat — dropping an in-between snapshot in favor of the newest one meant a run that cycled
    // Generating -> Completed -> Generating (processing the next queued item) skipped straight
    // over the Completed snapshot, so the UI only ever caught up once the whole queue drained.
    //
    // Dispatch is also no longer scheduled via requestAnimationFrame — rAF is unreliable in this
    // app's environment (doesn't fire promptly, or at all, in some conditions/backgrounded tabs),
    // which compounded the same symptom: even a snapshot that DID make it into `latest` could sit
    // unprocessed indefinitely waiting for a frame that never came. Dispatching directly off the
    // event (a plain async call, not requestAnimationFrame) processes each entry as soon as the
    // previous one finishes.
    const queue = [];
    let dispatching = false;
    const dispatch = async () => {
        if (dispatching) return;
        dispatching = true;
        try {
            while (queue.length > 0) {
                const snapshot = queue.shift();
                await dotNetReference.invokeMethodAsync("OnRunSnapshot", snapshot);
            }
        } finally {
            dispatching = false;
        }
    };
    source.addEventListener("snapshot", event => {
        queue.push(event.data);
        dispatch();
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
