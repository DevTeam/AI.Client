// Places a picker opened from the chat or the composer next to what opened it, as a popover
// rather than a dialog in the middle of a dimmed window. The opener is the element that had the
// focus when the picker appeared; a menu item that opened it is gone by then, so the composer is
// the fallback, and with neither the picker is centred. The picker stays hidden (CSS) until it has
// been placed here, so it never shows in the wrong place first.
const MARGIN = 12;
const GAP = 8;

function anchorFor(panel) {
    const focused = document.activeElement;
    if (focused instanceof HTMLElement && focused !== document.body && focused.isConnected && !panel.contains(focused)) return focused;
    return document.querySelector(".workspace-composer");
}

export function place(panel) {
    const anchor = anchorFor(panel);
    const apply = () => {
        if (!panel.isConnected) return;
        const width = panel.offsetWidth;
        const height = panel.offsetHeight;
        const maxLeft = Math.max(MARGIN, window.innerWidth - width - MARGIN);
        const maxTop = Math.max(MARGIN, window.innerHeight - height - MARGIN);
        let left = (window.innerWidth - width) / 2;
        let top = (window.innerHeight - height) / 2;
        if (anchor?.isConnected) {
            const rect = anchor.getBoundingClientRect();
            const above = rect.top - GAP - MARGIN;
            const below = window.innerHeight - rect.bottom - GAP - MARGIN;
            top = height <= above || above > below ? rect.top - GAP - height : rect.bottom + GAP;
            left = rect.left;
        }
        panel.style.left = `${Math.min(maxLeft, Math.max(MARGIN, left))}px`;
        panel.style.top = `${Math.min(maxTop, Math.max(MARGIN, top))}px`;
        panel.dataset.placed = "true";
    };
    apply();
    window.addEventListener("resize", apply);
    return { dispose: () => window.removeEventListener("resize", apply) };
}
