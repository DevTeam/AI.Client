const legacyStorageKey = "ai-client.workspace-layout.v1";

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

export function revealElement(id) {
    document.getElementById(id)?.scrollIntoView({ block: "nearest" });
}

// Both side panels share one behaviour; they differ in the edge they grow from and in their limits.
// A panel may grow up to its cap, as long as the conversation keeps conversationMin beside it.
const panels = {
    left: { min: 220, cap: 640, widthVar: "--sidebar-width", name: "Sidebar" },
    right: { min: 260, cap: 960, widthVar: "--widgets-width", name: "Chat widgets" }
};
const conversationMin = 400;
// Dragging this far past the minimum hides a panel; the stop at the minimum comes first, so a
// narrow panel is not lost by overshooting it.
const collapseOvershoot = 70;
const keyboardStep = 16;
// A press that moves less than this is a click on the divider, not a drag.
const dragSlop = 3;
const toggleDuration = 150;
// The phone layout has no room beside the conversation: both panels slide over it as drawers.
const phoneQuery = "(max-width: 900px)";
// A swipe has to travel this far, mostly sideways, to open or close a drawer.
const swipeDistance = 60;
// On phone width a mouse resting this close to the window's edge for peekDelay brings that drawer
// out; once the pointer has left the drawer, it goes back after peekLeaveDelay.
const peekEdge = 12;
const peekDelay = 200;
const peekLeaveDelay = 300;
// Rows that take the person somewhere else; on phone width the drawer they sit in steps aside.
// A row marked data-stays-in-drawer only changes the list itself (folds or unfolds it), so the
// drawer stays where it is.
const navigationSelector = ".chat-link, .search-nav-action, .archive-nav-item, .sidebar-global-nav .workspace-nav-item";
const staysInDrawerSelector = "[data-stays-in-drawer]";

/**
 * The left panel's visibility is a layout preference kept here, as the sidebar-collapsed class.
 * The right panel is the chat widget column, which Blazor renders only while it is open, so its
 * state is Blazor's: the shell carries it as data-widgets, and changes are asked of the page
 * through callbacks.SetChatWidgetsOpen.
 */
export function attach(workspace, callbacks, saved) {
    // The panels are kept in the client settings (callbacks.SaveWorkspacePanels), which Desktop
    // restores from its profile. Before that they lived in a localStorage entry of their own; it is
    // read once when the settings have nothing yet, and then dropped.
    let stored = saved ?? null;
    if (!stored) {
        try {
            const legacy = JSON.parse(localStorage.getItem(legacyStorageKey) || "null");
            if (legacy) stored = { sidebarWidth: legacy.left, chatWidgetsWidth: legacy.right, sidebarCollapsed: legacy.leftCollapsed === true };
        } catch {
            // An unreadable entry is no worse than none.
        }
    }
    try { localStorage.removeItem(legacyStorageKey); } catch { /* storage unavailable */ }
    stored ??= {};
    if (stored.sidebarWidth) workspace.style.setProperty(panels.left.widthVar, `${stored.sidebarWidth}px`);
    if (stored.chatWidgetsWidth) workspace.style.setProperty(panels.right.widthVar, `${stored.chatWidgetsWidth}px`);

    // A layout taken over from the old entry is saved as soon as the panels are set up.
    let savedJson = saved ? JSON.stringify(stored) : null;
    const save = () => {
        const next = {
            sidebarWidth: Math.round(widthOf("left")),
            chatWidgetsWidth: Math.round(widthOf("right")),
            sidebarCollapsed: workspace.classList.contains("sidebar-collapsed")
        };
        const json = JSON.stringify(next);
        if (json === savedJson) return;
        savedJson = json;
        callbacks?.invokeMethodAsync("SaveWorkspacePanels", next).catch(() => { savedJson = null; });
    };
    // A width the stylesheet set is in rem until the first drag writes pixels over it.
    const widthOf = side => {
        const value = getComputedStyle(workspace).getPropertyValue(panels[side].widthVar).trim();
        const scale = value.endsWith("rem") ? parseFloat(getComputedStyle(document.documentElement).fontSize) : 1;
        return parseFloat(value) * scale || panels[side].min;
    };
    const isOpen = side => side === "left"
        ? !workspace.classList.contains("sidebar-collapsed")
        : workspace.dataset.widgets === "open";
    const maxOf = side => {
        const other = side === "left" ? "right" : "left";
        const taken = isOpen(other) ? widthOf(other) : 0;
        return Math.max(panels[side].min, Math.min(panels[side].cap, workspace.clientWidth - taken - conversationMin));
    };
    const describe = () => {
        for (const separator of workspace.querySelectorAll(".workspace-resizer")) {
            const side = separator.dataset.side;
            const open = isOpen(side);
            const width = Math.round(widthOf(side));
            separator.setAttribute("aria-valuemax", String(Math.round(maxOf(side))));
            separator.setAttribute("aria-valuenow", open ? String(width) : "0");
            separator.setAttribute("aria-valuetext", open ? `${width} pixels` : `${panels[side].name} hidden`);
        }
        for (const button of workspace.querySelectorAll("[data-sidebar-toggle]")) {
            button.setAttribute("aria-pressed", String(isOpen("left")));
        }
    };

    // The page answers a request by re-rendering data-widgets; until then the last request stands,
    // so a drag that crosses the collapse point does not ask the same thing on every move.
    let requestedWidgets = null;
    const requestWidgets = open => {
        if (open === (requestedWidgets ?? isOpen("right")) || workspace.dataset.widgets === "unavailable") return;
        requestedWidgets = open;
        callbacks?.invokeMethodAsync("SetChatWidgetsOpen", open).catch(() => { requestedWidgets = null; });
    };
    const setOpen = (side, open) => {
        if (side === "left") workspace.classList.toggle("sidebar-collapsed", !open);
        else requestWidgets(open);
    };

    let animation = 0;
    // Only a deliberate toggle animates; a drag follows the pointer as it is.
    const animate = () => {
        workspace.classList.add("panels-animating");
        clearTimeout(animation);
        animation = setTimeout(() => workspace.classList.remove("panels-animating"), toggleDuration + 50);
    };
    const toggle = (side, open) => {
        if (open === isOpen(side)) return;
        // The widget column animates when the page renders it, see the observer below.
        if (side === "left") animate();
        setOpen(side, open);
        describe();
        save();
    };

    workspace.classList.toggle("sidebar-collapsed", stored.sidebarCollapsed === true);
    describe();
    if (!saved && Object.keys(stored).length) save();

    let widgetsState = workspace.dataset.widgets;
    const observer = new MutationObserver(() => {
        const state = workspace.dataset.widgets;
        if (state === widgetsState) return;
        const changedOpen = (widgetsState === "open") !== (state === "open");
        widgetsState = state;
        requestedWidgets = null;
        if (changedOpen && !workspace.classList.contains("is-resizing")) animate();
        if (state !== "open") workspace.classList.remove("phone-right-open");
        describe();
    });
    observer.observe(workspace, { attributes: true, attributeFilter: ["data-widgets"] });

    // A row's menu drops below the row, inside the sidebar's scrolling list; near the bottom of
    // that list it was cut off by the list's edge and hidden under the settings row. The menu opens
    // upwards when it does not fit below and there is more room above, and scrolls within itself
    // when it fits neither way. Measured against the row, so its own placement never feeds back.
    const placeSidebarMenus = () => {
        for (const menu of workspace.querySelectorAll(".workspace-sidebar .chat-menu")) {
            const row = menu.parentElement;
            const list = menu.closest(".sidebar-projects");
            if (!row || !list) continue;
            const bounds = list.getBoundingClientRect();
            const anchor = row.getBoundingClientRect();
            const below = bounds.bottom - anchor.bottom;
            const above = anchor.top - bounds.top;
            const height = menu.scrollHeight;
            const up = height > below && above > below;
            menu.classList.toggle("opens-up", up);
            const room = Math.max(up ? above : below, 0) - 8;
            menu.style.maxHeight = height > room ? `${Math.max(room, 120)}px` : "";
            // Anchored to the row's right edge, a narrow sidebar would clip its left part too.
            const width = anchor.right - bounds.left - 4;
            menu.style.maxWidth = "";
            // max-width counts the content box; the menu's padding and border come on top of it.
            const chrome = menu.offsetWidth - parseFloat(getComputedStyle(menu).width);
            if (width < menu.offsetWidth) menu.style.maxWidth = `${width - chrome}px`;
        }
    };
    const sidebar = workspace.querySelector(".workspace-sidebar");
    const menuObserver = new MutationObserver(placeSidebarMenus);
    if (sidebar) menuObserver.observe(sidebar, { childList: true, subtree: true });

    const listeners = [];
    const listen = (element, type, listener, options) => {
        element.addEventListener(type, listener, options);
        listeners.push([element, type, listener, options]);
    };
    const separatorOf = event => event.target instanceof Element ? event.target.closest(".workspace-resizer") : null;

    // Every press on the captured divider ends in a click on it, so two quick drags from the same
    // spot (the panel stopped at its maximum and is pulled again) read as a double-click; one
    // counts only when neither press moved.
    const pressesDragged = { left: [false, false], right: [false, false] };

    // The right divider comes and goes with the project, so the dividers are found by delegation.
    listen(workspace, "pointerdown", event => {
        const separator = separatorOf(event);
        if (!separator || event.button !== 0) return;
        const side = separator.dataset.side;
        const panel = panels[side];
        separator.setPointerCapture(event.pointerId);
        const startX = event.clientX;
        // Hiding by drag passes the minimum on the way; the panel comes back at the width it had.
        const startWidth = widthOf(side);
        const max = maxOf(side);
        let dragged = false;
        workspace.classList.add("is-resizing");
        const move = moveEvent => {
            if (!dragged && Math.abs(moveEvent.clientX - startX) < dragSlop) return;
            dragged = true;
            const bounds = workspace.getBoundingClientRect();
            const reach = side === "left" ? moveEvent.clientX - bounds.left : bounds.right - moveEvent.clientX;
            const open = reach >= panel.min - collapseOvershoot;
            setOpen(side, open);
            const width = open ? Math.max(panel.min, Math.min(max, reach)) : startWidth;
            workspace.style.setProperty(panel.widthVar, `${width}px`);
            describe();
        };
        const up = () => {
            separator.removeEventListener("pointermove", move);
            separator.removeEventListener("pointerup", up);
            separator.removeEventListener("pointercancel", up);
            workspace.classList.remove("is-resizing");
            pressesDragged[side] = [pressesDragged[side][1], dragged];
            // A click on the edge strip of a hidden panel brings it back at its last width.
            if (!dragged && !isOpen(side)) {
                toggle(side, true);
                return;
            }
            save();
        };
        separator.addEventListener("pointermove", move);
        separator.addEventListener("pointerup", up);
        separator.addEventListener("pointercancel", up);
    });
    listen(workspace, "dblclick", event => {
        const separator = separatorOf(event);
        if (!separator) return;
        const side = separator.dataset.side;
        if (!pressesDragged[side].some(Boolean)) toggle(side, !isOpen(side));
    });
    listen(workspace, "keydown", event => {
        const separator = separatorOf(event);
        if (!separator) return;
        const side = separator.dataset.side;
        const panel = panels[side];
        const open = isOpen(side);
        if (event.key === "Enter" || event.key === " ") {
            toggle(side, !open);
        } else if (event.key === "ArrowLeft" || event.key === "ArrowRight") {
            // Each panel grows away from its own edge.
            const grow = (event.key === "ArrowRight") === (side === "left");
            if (!open) {
                if (grow) toggle(side, true);
            } else if (!grow && widthOf(side) <= panel.min) {
                toggle(side, false);
            } else {
                const value = Math.max(panel.min, Math.min(maxOf(side), widthOf(side) + (grow ? keyboardStep : -keyboardStep)));
                workspace.style.setProperty(panel.widthVar, `${value}px`);
                describe();
                save();
            }
        } else {
            return;
        }
        event.preventDefault();
    });

    // Phone width: both panels start folded each time the window gets there, whatever they were
    // on a wide screen, and come out as drawers over the conversation. The wide-screen layout is
    // left as it was, so widening the window again brings it back.
    const phone = window.matchMedia(phoneQuery);
    const drawerOpen = () => workspace.classList.contains("phone-left-open") || workspace.classList.contains("phone-right-open");
    const openDrawer = side => {
        if (side === "right" && workspace.dataset.widgets === "unavailable") return;
        // Whatever opens or closes a drawer ends a hover peek; the peek sets itself again after.
        peeking = null;
        clearTimeout(peekTimer);
        workspace.classList.toggle("phone-left-open", side === "left");
        workspace.classList.toggle("phone-right-open", side === "right");
        if (side === "right") requestWidgets(true);
    };
    listen(phone, "change", () => openDrawer(null));
    const finishSwipe = (dx, dy) => {
        if (Math.abs(dx) < swipeDistance || Math.abs(dy) > Math.abs(dx) * 0.6) return;
        const towardsRight = dx > 0;
        if (workspace.classList.contains("phone-left-open")) {
            if (!towardsRight) openDrawer(null);
        } else if (workspace.classList.contains("phone-right-open")) {
            if (towardsRight) openDrawer(null);
        } else {
            openDrawer(towardsRight ? "left" : "right");
        }
    };
    // Code blocks and wide tables scroll sideways under a finger; a swipe there is theirs.
    const scrollsSideways = target => {
        for (let element = target instanceof Element ? target : null; element && element !== workspace; element = element.parentElement) {
            if (element.scrollWidth > element.clientWidth + 1 && /(auto|scroll)/.test(getComputedStyle(element).overflowX)) return true;
        }
        return false;
    };
    let swipe = null;
    listen(workspace, "touchstart", event => {
        swipe = null;
        if (!phone.matches || event.touches.length !== 1 || scrollsSideways(event.target)) return;
        swipe = { x: event.touches[0].clientX, y: event.touches[0].clientY };
    }, { passive: true });
    listen(workspace, "touchend", event => {
        if (!swipe) return;
        const touch = event.changedTouches[0];
        finishSwipe(touch.clientX - swipe.x, touch.clientY - swipe.y);
        swipe = null;
    }, { passive: true });
    listen(workspace, "touchcancel", () => { swipe = null; }, { passive: true });
    // A mouse resting at the window's left or right edge brings that drawer out over the chat for
    // as long as the pointer stays on it. The rest comes first, so a pointer only passing the edge
    // does not; leaving the drawer puts it away after a moment, unless one of its menus is open.
    let peeking = null;
    let pendingPeek = null;
    let peekTimer = 0;
    const edgeOf = event => {
        const bounds = workspace.getBoundingClientRect();
        if (event.clientX - bounds.left < peekEdge) return "left";
        if (bounds.right - event.clientX < peekEdge && workspace.dataset.widgets !== "unavailable") return "right";
        return null;
    };
    listen(workspace, "pointermove", event => {
        if (!phone.matches || event.pointerType !== "mouse" || event.buttons !== 0) return;
        const edge = edgeOf(event);
        if (!drawerOpen()) {
            if (edge === pendingPeek) return;
            clearTimeout(peekTimer);
            pendingPeek = edge;
            if (edge) {
                peekTimer = setTimeout(() => {
                    pendingPeek = null;
                    if (drawerOpen()) return;
                    openDrawer(edge);
                    peeking = edge;
                }, peekDelay);
            }
            return;
        }
        if (!peeking) return;
        clearTimeout(peekTimer);
        const drawer = peeking === "left" ? ".workspace-sidebar" : ".chat-widgets";
        const over = edge === peeking || (event.target instanceof Element && event.target.closest(drawer));
        if (!over && !document.querySelector('.workspace-sidebar [role="menu"], .chat-widgets-menu, .context-menu-backdrop')) {
            peekTimer = setTimeout(() => { if (peeking) openDrawer(null); }, peekLeaveDelay);
        }
    });

    listen(workspace, "click", event => {
        if (!(event.target instanceof Element)) return;
        // The header button re-renders with Blazor, so its clicks are picked up here by delegation.
        if (event.target.closest("[data-sidebar-toggle]")) {
            toggle("left", !isOpen("left"));
        } else if (event.target.closest(".workspace-phone-scrim")) {
            openDrawer(null);
        } else if (phone.matches && event.target.closest(".workspace-sidebar") && event.target.closest(navigationSelector)
            && !event.target.closest(staysInDrawerSelector)) {
            openDrawer(null);
        }
    });
    listen(document, "keydown", event => {
        const ctrlOnly = event.ctrlKey && !event.altKey && !event.shiftKey && !event.metaKey;
        if (ctrlOnly && event.code === "KeyB") {
            event.preventDefault();
            if (phone.matches) openDrawer(workspace.classList.contains("phone-left-open") ? null : "left");
            else toggle("left", !isOpen("left"));
        } else if (ctrlOnly && event.code === "KeyK") {
            // An open search panel takes the focus back; a closed one is opened through its
            // sidebar button (which works while the sidebar is hidden), and Blazor focuses it.
            event.preventDefault();
            const input = document.querySelector(".search-drawer-input");
            if (input) {
                input.focus();
                input.select();
            } else {
                workspace.querySelector("[data-search-open]")?.click();
            }
        } else if (event.key === "Escape" && phone.matches && drawerOpen() && !document.querySelector('[aria-modal="true"], dialog[open]')) {
            openDrawer(null);
        } else {
            pressHotkey(event);
        }
    });
    // The other shortcuts are declared on the controls they press: data-hotkey lists combinations
    // such as "Alt+Digit1" or "Ctrl+Comma", separated by spaces. The physical key (event.code) is
    // matched, not the character, so they work the same under a Russian layout.
    const comboOf = event => [
        event.ctrlKey && "Ctrl", event.altKey && "Alt", event.shiftKey && "Shift", event.metaKey && "Meta", event.code
    ].filter(Boolean).join("+");
    const pressHotkey = event => {
        // A bare key stays with whatever has focus; only chords are the workspace's.
        if (event.repeat || event.defaultPrevented || !(event.ctrlKey || event.altKey || event.metaKey)) return;
        // Nothing behind a modal dialog is reachable by pointer, so it is not by keyboard either.
        if (document.querySelector('[aria-modal="true"], dialog[open]')) return;
        const combo = comboOf(event);
        // Side panels (settings, permissions) render beside the shell, not inside it.
        const control = [...workspace.querySelectorAll("[data-hotkey]"), ...document.querySelectorAll(".permissions-drawer [data-hotkey]")]
            .find(element => element.dataset.hotkey.split(" ").includes(combo));
        if (!control || control.disabled) return;
        event.preventDefault();
        // click() reaches a control that is only hidden, such as a row of a collapsed sidebar or
        // a hover-only button, exactly as a pointer press on it would.
        control.click();
    };
    // Up, Down and Enter in the search box walk and open the results (Blazor handles the keys);
    // left alone, Up and Down would also throw the caret to either end of the query.
    listen(document, "keydown", event => {
        if (!(event.target instanceof HTMLElement) || !event.target.matches(".search-drawer-input")) return;
        if (event.altKey || event.ctrlKey || event.metaKey) return;
        if (event.key === "ArrowDown" || event.key === "ArrowUp" || event.key === "Enter") event.preventDefault();
    });

    return {
        dispose: () => {
            clearTimeout(animation);
            observer.disconnect();
            menuObserver.disconnect();
            listeners.forEach(([element, type, listener, options]) => element.removeEventListener(type, listener, options));
        }
    };
}
