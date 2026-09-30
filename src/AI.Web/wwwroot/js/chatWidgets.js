// Reordering of the chat widget column. Pointer events rather than HTML5 drag and drop, for the
// same reasons as the chat list: touch support, no browser ghost, and a say over the drop line.
// A widget is picked up only by its handle, so the rest of its header stays clickable and a
// press on the handle never needs a long-press to tell a drag from a scroll.

const widgetSelector = ".chat-widget[data-widget-id]";
const handleSelector = ".chat-widget-handle";
// A press that moves less than this is a click on the handle, not a drag.
const slop = 4;
const autoScrollEdge = 32;
const autoScrollStep = 10;

export function attach(list, dotNet) {
    let press = null;
    let drag = null;

    const widgets = () => [...list.querySelectorAll(widgetSelector)];

    function onPointerDown(event) {
        if (event.button !== 0 || drag) return;
        const handle = event.target.closest(handleSelector);
        const widget = handle?.closest(widgetSelector);
        if (!widget || !list.contains(widget)) return;
        press = { handle, widget, pointerId: event.pointerId, y: event.clientY };
        handle.setPointerCapture(event.pointerId);
    }

    function onPointerMove(event) {
        if (!press || event.pointerId !== press.pointerId) return;
        if (!drag) {
            if (Math.abs(event.clientY - press.y) < slop) return;
            drag = { widget: press.widget, startY: press.y, scrollTop: list.scrollTop, beforeId: undefined };
            drag.widget.classList.add("is-dragging");
            list.classList.add("is-reordering");
        }
        event.preventDefault();
        autoScroll(event.clientY);
        const offset = event.clientY - drag.startY + (list.scrollTop - drag.scrollTop);
        drag.widget.style.transform = `translateY(${offset}px)`;
        mark(target(event.clientY));
    }

    // The widget the dragged one would land before, or null for the end of the column.
    function target(y) {
        for (const widget of widgets()) {
            if (widget === drag.widget) continue;
            const rect = widget.getBoundingClientRect();
            if (y < rect.top + rect.height / 2) return widget;
        }
        return null;
    }

    function mark(before) {
        const beforeId = before?.dataset.widgetId ?? null;
        if (beforeId === drag.beforeId) return;
        drag.beforeId = beforeId;
        for (const widget of widgets()) widget.classList.toggle("drop-before", widget === before);
        list.classList.toggle("drop-end", before === null);
    }

    function autoScroll(y) {
        const rect = list.getBoundingClientRect();
        if (y < rect.top + autoScrollEdge) list.scrollTop -= autoScrollStep;
        else if (y > rect.bottom - autoScrollEdge) list.scrollTop += autoScrollStep;
    }

    function finish(event, drop) {
        if (!press || event.pointerId !== press.pointerId) return;
        const finished = drag;
        if (press.handle.hasPointerCapture(event.pointerId)) press.handle.releasePointerCapture(event.pointerId);
        press = null;
        drag = null;
        if (!finished) return;
        finished.widget.classList.remove("is-dragging");
        finished.widget.style.transform = "";
        list.classList.remove("is-reordering", "drop-end");
        for (const widget of widgets()) widget.classList.remove("drop-before");
        const id = finished.widget.dataset.widgetId;
        // Dropping where it already was is no move; nor is a drop that decided nothing.
        const all = widgets();
        const nextId = all[all.indexOf(finished.widget) + 1]?.dataset.widgetId ?? null;
        const unchanged = finished.beforeId === undefined || finished.beforeId === id || finished.beforeId === nextId;
        if (drop && !unchanged) dotNet.invokeMethodAsync("OnWidgetDropped", id, finished.beforeId);
    }

    const onPointerUp = event => finish(event, true);
    const onPointerCancel = event => finish(event, false);

    list.addEventListener("pointerdown", onPointerDown);
    list.addEventListener("pointermove", onPointerMove);
    list.addEventListener("pointerup", onPointerUp);
    list.addEventListener("pointercancel", onPointerCancel);

    return {
        dispose() {
            list.removeEventListener("pointerdown", onPointerDown);
            list.removeEventListener("pointermove", onPointerMove);
            list.removeEventListener("pointerup", onPointerUp);
            list.removeEventListener("pointercancel", onPointerCancel);
        }
    };
}
