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
 * reached through its project's row. Nothing is drawn under reduced motion.
 */
export function playCursor(projectId, chatId, durationMs) {
    cancelCursor();
    if (reducedMotion()) return;
    const target = targetRow(projectId, chatId);
    const origin = document.querySelector(".composer-input") || document.querySelector(".workspace-location");
    if (!target || !origin) return;
    const from = origin.getBoundingClientRect();
    const to = target.getBoundingClientRect();
    const element = document.createElement("div");
    element.className = "ghost-cursor";
    element.setAttribute("aria-hidden", "true");
    element.innerHTML = '<svg viewBox="0 0 24 24" width="22" height="22"><path d="M4 3l7 17 2.5-7L21 10.5z"/></svg>';
    element.style.transform = `translate(${from.left + from.width / 2}px, ${from.top + from.height / 2}px)`;
    document.body.appendChild(element);
    cursor = element;
    // Read the start position back so the move below is a transition, not a jump.
    void element.getBoundingClientRect();
    const travel = Math.max(400, durationMs - 700);
    element.style.transitionDuration = `${travel}ms`;
    element.style.transform = `translate(${to.left + Math.min(28, to.width / 3)}px, ${to.top + to.height / 2}px)`;
    timers.push(setTimeout(() => {
        element.classList.add("is-pressing");
        target.classList.add("ghost-cursor-target");
        const ripple = document.createElement("span");
        ripple.className = "ghost-cursor-ripple";
        element.appendChild(ripple);
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
