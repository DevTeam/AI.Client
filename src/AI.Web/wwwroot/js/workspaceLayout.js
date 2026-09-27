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

const sidebarMin = 220;
const sidebarMax = 420;
// Dragging this far past the minimum hides the sidebar; the stop at the minimum comes first, so a
// narrow panel is not lost by overshooting it.
const collapseOvershoot = 70;
const keyboardStep = 16;
// A press that moves less than this is a click on the divider, not a drag.
const dragSlop = 3;
const toggleDuration = 150;

export function attach(workspace) {
    let stored = {};
    try {
        stored = JSON.parse(localStorage.getItem(storageKey) || "null") || {};
    } catch {
        localStorage.removeItem(storageKey);
    }
    if (stored.left) workspace.style.setProperty("--sidebar-width", `${stored.left}px`);
    if (stored.right) workspace.style.setProperty("--settings-width", `${stored.right}px`);

    const save = () => {
        const styles = getComputedStyle(workspace);
        const width = name => parseFloat(styles.getPropertyValue(name)) || undefined;
        stored = {
            ...stored,
            left: width("--sidebar-width"),
            right: width("--settings-width"),
            leftCollapsed: workspace.classList.contains("sidebar-collapsed")
        };
        localStorage.setItem(storageKey, JSON.stringify(stored));
    };
    const sidebarWidth = () => parseFloat(getComputedStyle(workspace).getPropertyValue("--sidebar-width")) || sidebarMin;
    const leftSeparator = workspace.querySelector('.workspace-resizer[data-side="left"]');
    const describe = () => {
        const collapsed = workspace.classList.contains("sidebar-collapsed");
        if (leftSeparator) {
            leftSeparator.setAttribute("aria-valuenow", collapsed ? "0" : String(Math.round(sidebarWidth())));
            leftSeparator.setAttribute("aria-valuetext", collapsed ? "Sidebar hidden" : `${Math.round(sidebarWidth())} pixels`);
        }
        for (const button of workspace.querySelectorAll("[data-sidebar-toggle]")) {
            button.setAttribute("aria-pressed", String(!collapsed));
        }
    };
    const setCollapsed = collapsed => workspace.classList.toggle("sidebar-collapsed", collapsed);
    let animation = 0;
    // Only a deliberate toggle animates; a drag follows the pointer as it is.
    const toggle = collapsed => {
        if (collapsed === workspace.classList.contains("sidebar-collapsed")) return;
        workspace.classList.add("sidebar-animating");
        clearTimeout(animation);
        animation = setTimeout(() => workspace.classList.remove("sidebar-animating"), toggleDuration + 50);
        setCollapsed(collapsed);
        describe();
        save();
    };
    const toggleSidebar = () => toggle(!workspace.classList.contains("sidebar-collapsed"));

    setCollapsed(stored.leftCollapsed === true);
    describe();

    // Every press on the captured divider ends in a click on it, so two quick drags from the same
    // spot (the sidebar stopped at its maximum and is pulled again) read as a double-click; one
    // counts only when neither press moved.
    let pressesDragged = [false, false];
    const listeners = [];
    const listen = (element, type, listener) => {
        element.addEventListener(type, listener);
        listeners.push([element, type, listener]);
    };

    for (const separator of workspace.querySelectorAll(".workspace-resizer")) {
        const side = separator.dataset.side;
        listen(separator, "pointerdown", event => {
            if (event.button !== 0) return;
            separator.setPointerCapture(event.pointerId);
            const startX = event.clientX;
            // Hiding by drag passes the minimum on the way; the sidebar comes back at the width it had.
            const startWidth = sidebarWidth();
            let dragged = false;
            workspace.classList.add("is-resizing");
            const move = moveEvent => {
                if (!dragged && Math.abs(moveEvent.clientX - startX) < dragSlop) return;
                dragged = true;
                const bounds = workspace.getBoundingClientRect();
                if (side !== "left") {
                    const value = Math.max(280, Math.min(520, bounds.right - moveEvent.clientX));
                    workspace.style.setProperty("--settings-width", `${value}px`);
                    return;
                }

                const x = moveEvent.clientX - bounds.left;
                const collapsed = x < sidebarMin - collapseOvershoot;
                setCollapsed(collapsed);
                const width = collapsed ? startWidth : Math.max(sidebarMin, Math.min(sidebarMax, x));
                workspace.style.setProperty("--sidebar-width", `${width}px`);
                describe();
            };
            const up = () => {
                separator.removeEventListener("pointermove", move);
                separator.removeEventListener("pointerup", up);
                separator.removeEventListener("pointercancel", up);
                workspace.classList.remove("is-resizing");
                pressesDragged = [pressesDragged[1], dragged];
                // A click on the edge strip of a hidden sidebar brings it back at its last width.
                if (!dragged && side === "left" && workspace.classList.contains("sidebar-collapsed")) {
                    toggle(false);
                    return;
                }
                save();
            };
            separator.addEventListener("pointermove", move);
            separator.addEventListener("pointerup", up);
            separator.addEventListener("pointercancel", up);
        });
    }

    if (leftSeparator) {
        listen(leftSeparator, "dblclick", () => {
            if (!pressesDragged.some(Boolean)) toggleSidebar();
        });
        listen(leftSeparator, "keydown", event => {
            const collapsed = workspace.classList.contains("sidebar-collapsed");
            if (event.key === "Enter" || event.key === " ") {
                toggleSidebar();
            } else if (event.key === "ArrowLeft" || event.key === "ArrowRight") {
                const grow = event.key === "ArrowRight";
                if (collapsed) {
                    if (grow) toggle(false);
                } else if (!grow && sidebarWidth() <= sidebarMin) {
                    toggle(true);
                } else {
                    const value = Math.max(sidebarMin, Math.min(sidebarMax, sidebarWidth() + (grow ? keyboardStep : -keyboardStep)));
                    workspace.style.setProperty("--sidebar-width", `${value}px`);
                    describe();
                    save();
                }
            } else {
                return;
            }
            event.preventDefault();
        });
    }

    // The header button re-renders with Blazor, so its clicks are picked up here by delegation.
    listen(workspace, "click", event => {
        if (event.target instanceof Element && event.target.closest("[data-sidebar-toggle]")) {
            toggleSidebar();
            describe();
        }
    });
    listen(document, "keydown", event => {
        if (event.ctrlKey && !event.altKey && !event.shiftKey && !event.metaKey && event.code === "KeyB") {
            event.preventDefault();
            toggleSidebar();
        }
    });

    return {
        dispose: () => {
            clearTimeout(animation);
            listeners.forEach(([element, type, listener]) => element.removeEventListener(type, listener));
        }
    };
}
