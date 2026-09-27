const storageKey = "ai-client.workspace-layout.v1";

export function forwardContextMenu(x, y, backdropSelector) {
    const backdrop = document.querySelector(backdropSelector);
    const previousPointerEvents = backdrop ? backdrop.style.pointerEvents : null;
    if (backdrop) backdrop.style.pointerEvents = "none";
    const target = document.elementFromPoint(x, y);
    if (backdrop) backdrop.style.pointerEvents = previousPointerEvents ?? "";
    if (target) {
        target.dispatchEvent(new MouseEvent("contextmenu", { bubbles: true, cancelable: true, clientX: x, clientY: y }));
    }
}

export function blurActiveElement() {
    if (document.activeElement instanceof HTMLElement) document.activeElement.blur();
}

export function focusAdjacentItem(containerSelector, itemSelector, key) {
    const container = document.querySelector(containerSelector);
    if (!container) return;
    const items = [...container.querySelectorAll(itemSelector)];
    if (!items.length) return;
    const currentIndex = items.indexOf(document.activeElement);
    let nextIndex;
    switch (key) {
        case "ArrowDown": nextIndex = currentIndex < 0 ? 0 : Math.min(currentIndex + 1, items.length - 1); break;
        case "ArrowUp": nextIndex = currentIndex < 0 ? items.length - 1 : Math.max(currentIndex - 1, 0); break;
        case "Home": nextIndex = 0; break;
        case "End": nextIndex = items.length - 1; break;
        default: return;
    }
    items[nextIndex].focus();
}

export function attach(workspace) {
    let stored = {};
    try {
        stored = JSON.parse(localStorage.getItem(storageKey) || "null") || {};
    } catch {
        localStorage.removeItem(storageKey);
    }
    if (stored) {
        if (stored.left) workspace.style.setProperty("--sidebar-width", `${stored.left}px`);
        if (stored.right) workspace.style.setProperty("--settings-width", `${stored.right}px`);
    }

    const listeners = [];
    for (const separator of workspace.querySelectorAll(".workspace-resizer")) {
        const side = separator.dataset.side;
        const down = event => {
            separator.setPointerCapture(event.pointerId);
            const move = moveEvent => {
                const bounds = workspace.getBoundingClientRect();
                const value = side === "left"
                    ? Math.max(220, Math.min(420, moveEvent.clientX - bounds.left))
                    : Math.max(280, Math.min(520, bounds.right - moveEvent.clientX));
                workspace.style.setProperty(side === "left" ? "--sidebar-width" : "--settings-width", `${value}px`);
            };
            const up = () => {
                separator.removeEventListener("pointermove", move);
                separator.removeEventListener("pointerup", up);
                const styles = getComputedStyle(workspace);
                stored = {
                    ...stored,
                    left: parseFloat(styles.getPropertyValue("--sidebar-width")),
                    right: parseFloat(styles.getPropertyValue("--settings-width"))
                };
                localStorage.setItem(storageKey, JSON.stringify(stored));
            };
            separator.addEventListener("pointermove", move);
            separator.addEventListener("pointerup", up);
        };
        separator.addEventListener("pointerdown", down);
        listeners.push([separator, down]);
    }

    return {
        dispose: () => listeners.forEach(([element, listener]) => element.removeEventListener("pointerdown", listener))
    };
}
