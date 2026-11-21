// Layout of the right-hand drawers (.permissions-drawer) that CSS alone cannot give them:
//
// - Width. A drawer is small or wide (is-wide); each size has one width, dragged at the drawer's
//   start edge and kept in this browser. Double-click on the edge returns to the default. The grab
//   strip lives outside Blazor's DOM and is kept in line with whichever drawer is open.
// - Swaps. When one drawer replaces another of a different width, the new one grows or shrinks
//   from the old width instead of jumping.
// - Docking. On a wide window a small drawer sits beside the conversation (CSS); the shell's width
//   change is animated here, and runEvents.watchDrawerDismiss leaves a docked drawer open on a
//   press in the chat.
// - Focus. An opened drawer takes the focus unless something inside already has it; a closed one
//   gives it back to what had it before, unless the close came from a press somewhere else.
// - Sheets. At phone width a drawer marked drawer-sheet rises from the bottom; dragging its header
//   down far enough closes it through its own close button.
(() => {
    const DRAWER = ".permissions-drawer";
    const STORAGE_KEY = "ai-client.drawer-widths.v1";
    const DOCK_QUERY = "(min-width: 100rem)";
    const PHONE_QUERY = "(max-width: 44rem)";
    // Limits in rem, so they scale with the root font size like the defaults do.
    const LIMITS = { s: { min: 22, max: 44, prop: "--drawer-width-s" }, l: { min: 40, max: 90, prop: "--drawer-width-l" } };
    const KEY_STEP = 16;
    const SHEET_CLOSE_DISTANCE = 80;
    // A close this soon after a press outside the drawer leaves the focus where that press put it.
    const OUTSIDE_PRESS_WINDOW_MS = 600;

    const root = document.documentElement;
    let active = null;
    let activeWidth = 0;
    let wasDocked = false;
    // The last few elements focused outside the drawers, newest last: a menu item that opened a
    // drawer is gone by the time it closes, so the restore falls back to what had the focus before.
    let openers = [];
    let lastOutsidePress = 0;
    let resizer = null;
    const sizeObserver = new ResizeObserver(() => { if (active) activeWidth = active.getBoundingClientRect().width; place(); });

    function remPx() { return parseFloat(getComputedStyle(root).fontSize) || 16; }

    function motionAllowed() {
        return !window.matchMedia("(prefers-reduced-motion: reduce)").matches && root.dataset.animations !== "false";
    }

    function slowDuration() {
        const value = getComputedStyle(root).getPropertyValue("--duration-slow").trim();
        return value.endsWith("ms") ? parseFloat(value) : parseFloat(value) * 1000 || 200;
    }

    function readStored() {
        try { return JSON.parse(localStorage.getItem(STORAGE_KEY) || "null") ?? {}; }
        catch { return {}; }
    }

    function writeStored(widths) {
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(widths)); }
        catch { /* storage unavailable: the width lasts until reload */ }
    }

    function applyStored() {
        const stored = readStored();
        for (const [size, limit] of Object.entries(LIMITS)) {
            if (typeof stored[size] === "number") root.style.setProperty(limit.prop, `${stored[size]}rem`);
            else root.style.removeProperty(limit.prop);
        }
    }

    function sizeOf(drawer) { return drawer.classList.contains("is-wide") ? "l" : "s"; }

    function findActive() {
        return [...document.querySelectorAll(`${DRAWER}:not(.is-leaving)`)].at(-1) ?? null;
    }

    function isDocked(drawer) {
        return !!drawer && window.matchMedia(DOCK_QUERY).matches && !drawer.classList.contains("is-wide");
    }

    function ensureResizer() {
        if (resizer?.isConnected) return resizer;
        resizer = document.createElement("div");
        resizer.className = "drawer-resizer";
        resizer.hidden = true;
        resizer.tabIndex = 0;
        resizer.setAttribute("role", "separator");
        resizer.setAttribute("aria-orientation", "vertical");
        resizer.setAttribute("aria-label", "Resize panel");
        resizer.title = "Drag to resize; double-click for the default width";
        resizer.addEventListener("pointerdown", startDrag);
        resizer.addEventListener("dblclick", resetWidth);
        resizer.addEventListener("keydown", onResizerKey);
        document.body.appendChild(resizer);
        return resizer;
    }

    function place() {
        const strip = ensureResizer();
        if (!active || !active.isConnected || window.matchMedia(PHONE_QUERY).matches) {
            strip.hidden = true;
            return;
        }
        strip.hidden = false;
        strip.style.left = `${active.getBoundingClientRect().left}px`;
        strip.setAttribute("aria-valuenow", String(Math.round(active.getBoundingClientRect().width)));
    }

    function setWidth(size, px) {
        const limit = LIMITS[size];
        const rem = remPx();
        const max = Math.min(limit.max, (window.innerWidth - 3 * rem) / rem);
        const value = Math.max(limit.min, Math.min(max, px / rem));
        root.style.setProperty(limit.prop, `${value.toFixed(2)}rem`);
        return value;
    }

    function save(size, value) {
        const stored = readStored();
        if (value === null) delete stored[size]; else stored[size] = Math.round(value * 100) / 100;
        writeStored(stored);
    }

    function startDrag(event) {
        if (!active || event.button !== 0) return;
        event.preventDefault();
        const drawer = active;
        const size = sizeOf(drawer);
        let value = null;
        try { resizer.setPointerCapture(event.pointerId); } catch { /* a pointer that is already gone */ }
        root.classList.add("is-resizing-drawer");
        const move = moveEvent => {
            value = setWidth(size, window.innerWidth - moveEvent.clientX);
            place();
        };
        const end = () => {
            resizer.removeEventListener("pointermove", move);
            resizer.removeEventListener("pointerup", end);
            resizer.removeEventListener("pointercancel", end);
            root.classList.remove("is-resizing-drawer");
            if (value !== null) save(size, value);
        };
        resizer.addEventListener("pointermove", move);
        resizer.addEventListener("pointerup", end);
        resizer.addEventListener("pointercancel", end);
    }

    function resetWidth() {
        if (!active) return;
        const size = sizeOf(active);
        root.style.removeProperty(LIMITS[size].prop);
        save(size, null);
    }

    function onResizerKey(event) {
        if (!active || (event.key !== "ArrowLeft" && event.key !== "ArrowRight")) return;
        event.preventDefault();
        const size = sizeOf(active);
        const current = active.getBoundingClientRect().width;
        save(size, setWidth(size, current + (event.key === "ArrowLeft" ? KEY_STEP : -KEY_STEP)));
    }

    function animateSwap(drawer, fromWidth) {
        const toWidth = drawer.getBoundingClientRect().width;
        if (!motionAllowed() || !fromWidth || Math.abs(toWidth - fromWidth) < 2) return;
        drawer.animate([{ width: `${fromWidth}px` }, { width: `${toWidth}px` }],
            { duration: slowDuration(), easing: "cubic-bezier(0.2, 0.8, 0.2, 1)" });
    }

    function animateDock() {
        const shell = document.querySelector(".workspace-shell");
        if (!shell || !motionAllowed()) return;
        shell.classList.add("dock-animating");
        setTimeout(() => shell.classList.remove("dock-animating"), slowDuration() + 50);
    }

    function focusInto(drawer) {
        // Blazor may focus a field of its own after the first render; give it the first turn.
        setTimeout(() => {
            if (!drawer.isConnected || drawer.classList.contains("is-leaving")) return;
            if (drawer.contains(document.activeElement)) return;
            if (!drawer.hasAttribute("tabindex")) drawer.setAttribute("tabindex", "-1");
            drawer.focus({ preventScroll: true });
        }, 60);
    }

    function remember(element) {
        if (!(element instanceof HTMLElement) || element === document.body || element.closest(`${DRAWER}, .drawer-resizer, .review-workspace`)) return;
        openers = [...openers.filter(item => item !== element && item.isConnected), element].slice(-5);
    }

    function restoreFocus() {
        const target = openers.filter(item => item.isConnected).at(-1);
        openers = [];
        if (Date.now() - lastOutsidePress < OUTSIDE_PRESS_WINDOW_MS) return;
        setTimeout(() => {
            const focused = document.activeElement;
            if (focused && focused !== document.body && !focused.closest(".drawer-leaving-layer")) return;
            if (target?.isConnected) target.focus({ preventScroll: true });
        }, 0);
    }

    function update() {
        const previous = active;
        const previousWidth = activeWidth;
        const next = findActive();
        if (next !== previous) {
            if (previous) sizeObserver.unobserve(previous);
            active = next;
            if (next) {
                sizeObserver.observe(next);
                next.addEventListener("animationend", place);
                next.addEventListener("transitionend", place);
                const swapped = !!previous && (!previous.isConnected || previous.classList.contains("is-leaving"));
                if (swapped) animateSwap(next, previousWidth);
                else {
                    remember(document.activeElement);
                    focusInto(next);
                }
                activeWidth = next.getBoundingClientRect().width;
            } else {
                activeWidth = 0;
                restoreFocus();
            }
        }
        const docked = isDocked(active);
        if (docked !== wasDocked) {
            wasDocked = docked;
            animateDock();
        }
        place();
    }

    // Sheets: the header drags the sheet down; past the threshold its close button closes it.
    function startSheetDrag(event) {
        if (event.button !== 0 || !window.matchMedia(PHONE_QUERY).matches) return;
        const header = event.target instanceof Element ? event.target.closest(".drawer-sheet > header") : null;
        if (!header || event.target.closest("button, a, input, select, textarea")) return;
        const sheet = header.parentElement;
        const startY = event.clientY;
        let distance = 0;
        try { header.setPointerCapture(event.pointerId); } catch { /* a pointer that is already gone */ }
        const move = moveEvent => {
            distance = Math.max(0, moveEvent.clientY - startY);
            sheet.style.transform = distance ? `translateY(${distance}px)` : "";
        };
        const end = () => {
            header.removeEventListener("pointermove", move);
            header.removeEventListener("pointerup", end);
            header.removeEventListener("pointercancel", end);
            if (distance >= SHEET_CLOSE_DISTANCE) {
                const close = sheet.querySelector(":scope > header .drawer-close");
                if (close) { close.click(); return; }
            }
            if (distance && motionAllowed()) {
                sheet.animate([{ transform: `translateY(${distance}px)` }, { transform: "translateY(0)" }],
                    { duration: slowDuration(), easing: "cubic-bezier(0.2, 0.8, 0.2, 1)" });
            }
            sheet.style.transform = "";
        };
        header.addEventListener("pointermove", move);
        header.addEventListener("pointerup", end);
        header.addEventListener("pointercancel", end);
    }

    function start() {
        applyStored();
        const app = document.getElementById("app");
        if (app) new MutationObserver(update).observe(app, { childList: true, subtree: true, attributes: true, attributeFilter: ["class"] });
        window.addEventListener("resize", update);
        window.matchMedia(DOCK_QUERY).addEventListener("change", update);
        document.addEventListener("focusin", event => remember(event.target));
        document.addEventListener("pointerdown", event => {
            const target = event.target instanceof Element ? event.target : null;
            if (active && target && !target.closest(`${DRAWER}, .drawer-resizer`)) lastOutsidePress = Date.now();
            // A press focuses what it lands on only once it ends; take the control it is on now.
            remember(target?.closest("button, a[href], input, select, textarea, [tabindex]"));
            startSheetDrag(event);
        }, true);
        // Another window of this client changed a width: follow it.
        window.addEventListener("storage", event => { if (event.key === STORAGE_KEY) applyStored(); });
        update();
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start, { once: true });
    else start();
})();
