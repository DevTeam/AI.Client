// Shows the user what happens when the assistant opens another project or chat for them: a
// pointer that travels to the sidebar row and presses it, and a highlight where the window lands.
// Timing runs on timers, not on animation events, so a page that never paints (a hidden pane,
// a background tab) still moves on; the visuals are CSS transitions on top of that.

let lastBusyInput = 0;
// Typing and scrolling say the user is reading or writing something. Clicks do not count: the click
// that approved the assistant's step is exactly the attention a move should follow.
for (const type of ["keydown", "wheel", "touchmove"]) {
    window.addEventListener(type, () => { lastBusyInput = Date.now(); }, { capture: true, passive: true });
}

const reducedMotion = () => window.matchMedia("(prefers-reduced-motion: reduce)").matches;

/** Whether moving the window now would break into something the user is doing. */
export function isUserBusy(quietMs) {
    if (Date.now() - lastBusyInput < quietMs) return true;
    const composer = document.querySelector(".composer-input textarea");
    if (composer && composer.value.trim().length > 0) return true;
    const focused = document.activeElement;
    if (!(focused instanceof HTMLElement) || focused === composer) return false;
    if (focused.isContentEditable) return true;
    return focused.matches("textarea, select, input:not([type=button]):not([type=checkbox]):not([type=radio]):not([type=submit])");
}

const CURSOR_HTML = '<span class="ghost-cursor-halo"></span>'
    + '<svg viewBox="0 0 24 24" width="26" height="26">'
    + '<path class="ghost-cursor-body" d="M4 3l7 17 2.5-7L21 10.5z"/>'
    + '<path class="ghost-cursor-shine" d="M5.9 5.9l4.9 11.6 1-2.9z"/></svg>';

/** Creates the virtual pointer; its tip sits exactly at the point its transform moves it to. */
export function createCursor(className = "") {
    const element = document.createElement("div");
    element.className = `ghost-cursor ${className}`.trim();
    element.setAttribute("aria-hidden", "true");
    element.innerHTML = CURSOR_HTML;
    return element;
}

/**
 * Leaves a fading trail behind the pointer while it travels. The trail follows painted frames,
 * so a page that never paints simply gets none; it stops by itself when the pointer leaves.
 * Returns a function that stops it early.
 */
export function startTrail(element) {
    if (reducedMotion() || typeof requestAnimationFrame !== "function") return () => { };
    element.classList.add("is-moving");
    let stopped = false, last = null;
    const step = () => {
        if (stopped || !element.isConnected || element.classList.contains("is-leaving")) {
            element.classList.remove("is-moving");
            return;
        }
        const { left: x, top: y } = element.getBoundingClientRect();
        if (!last || Math.hypot(x - last.x, y - last.y) >= 7) {
            if (last) {
                const dot = document.createElement("span");
                dot.className = "ghost-cursor-trail";
                dot.setAttribute("aria-hidden", "true");
                dot.style.transform = `translate(${x}px, ${y}px)`;
                document.body.appendChild(dot);
                setTimeout(() => dot.remove(), 600);
            }
            last = { x, y };
        }
        requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
    return () => { stopped = true; element.classList.remove("is-moving"); };
}

/**
 * Plays a press: the pointer dips and springs back, rings and sparks burst from the tip, and the
 * target, when given, answers like a pressed control.
 */
export function pressCursor(element, target = null) {
    element.querySelectorAll(".ghost-cursor-ripple,.ghost-cursor-flash,.ghost-cursor-spark").forEach(item => item.remove());
    element.classList.remove("is-pressing", "is-released");
    void element.getBoundingClientRect();
    element.classList.add("is-pressing");
    const add = (className, style) => {
        const span = document.createElement("span");
        span.className = className;
        if (style) for (const [key, value] of Object.entries(style)) span.style.setProperty(key, value);
        element.appendChild(span);
        setTimeout(() => span.remove(), 900);
    };
    add("ghost-cursor-flash");
    add("ghost-cursor-ripple");
    if (!reducedMotion()) {
        add("ghost-cursor-ripple is-echo");
        const count = 8;
        for (let i = 0; i < count; i++)
            add("ghost-cursor-spark", { "--spark-angle": `${i * 360 / count + 22.5}deg`, "--spark-distance": `${18 + (i % 2) * 8}px` });
    }
    setTimeout(() => {
        element.classList.remove("is-pressing");
        element.classList.add("is-released");
    }, 180);
    if (target) {
        target.classList.remove("ghost-cursor-pressed");
        void target.getBoundingClientRect();
        target.classList.add("ghost-cursor-pressed");
        setTimeout(() => target.classList.remove("ghost-cursor-pressed"), 650);
    }
}

function targetRow(projectId, chatId) {
    return (chatId && document.querySelector(`.chat-nav-row[data-chat-id="${chatId}"]`))
        || document.querySelector(`.project-nav-row[data-project-id="${projectId}"]`);
}

let cursor = null;
let timers = [];

function clearTimers() {
    for (const timer of timers) clearTimeout(timer);
    timers = [];
}

/** Removes the pointer, if one is on screen. */
export function cancelCursor() {
    clearTimers();
    const current = cursor;
    cursor = null;
    if (!current) return;
    current.classList.add("is-leaving");
    setTimeout(() => current.remove(), 250);
}

/**
 * Moves a pointer from the composer to the row of the target, then presses it just before the
 * countdown ends. The chat row is used when it is in the list; a chat of another project is
 * reached through its project's row. Under reduced motion the pointer does not travel: it appears
 * at the row, where it presses with a still ring. It stays because it is the part that says where
 * the window is going; Windows with animations switched off reports reduced motion to the page.
 */
export function playCursor(projectId, chatId, durationMs) {
    cancelCursor();
    const still = reducedMotion();
    const target = targetRow(projectId, chatId);
    const origin = document.querySelector(".composer-input") || document.querySelector(".workspace-location");
    if (!target || !origin) return;
    const from = origin.getBoundingClientRect();
    const to = target.getBoundingClientRect();
    const element = createCursor(still ? "is-still" : "");
    const end = `translate(${to.left + Math.min(28, to.width / 3)}px, ${to.top + to.height / 2}px)`;
    element.style.transform = still ? end : `translate(${from.left + from.width / 2}px, ${from.top + from.height / 2}px)`;
    document.body.appendChild(element);
    cursor = element;
    const travel = still ? Math.max(400, durationMs - 1200) : Math.max(400, durationMs - 700);
    if (!still) {
        // Read the start position back so the move below is a transition, not a jump.
        void element.getBoundingClientRect();
        element.style.transitionDuration = `${travel}ms`;
        element.style.transform = end;
        const stopTrail = startTrail(element);
        timers.push(setTimeout(stopTrail, travel));
    }
    timers.push(setTimeout(() => {
        target.classList.add("ghost-cursor-target");
        pressCursor(element, target);
    }, travel + 150));
    timers.push(setTimeout(() => target.classList.remove("ghost-cursor-target"), durationMs + 400));
}

/** Marks where the window landed: the row in the sidebar and the location in the header. */
export function highlightArrival(projectId, chatId) {
    cancelCursor();
    const marks = [targetRow(projectId, chatId), document.querySelector(".workspace-location")].filter(Boolean);
    for (const mark of marks) {
        mark.classList.remove("navigation-arrived");
        void mark.getBoundingClientRect();
        mark.classList.add("navigation-arrived");
        setTimeout(() => mark.classList.remove("navigation-arrived"), 2200);
    }
    marks[0]?.scrollIntoView({ block: "nearest", behavior: reducedMotion() ? "auto" : "smooth" });
}
