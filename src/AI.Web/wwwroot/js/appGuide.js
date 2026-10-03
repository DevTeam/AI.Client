import { cancelCursor, createCursor, pressCursor, startTrail } from "./navigationCue.js";

let active = null;
let positionedRequest = null;
let cursorPosition = null;
let pointerPosition = null;
let pointedRequest = null;
let layoutObserver = null;
let lastActivity = Date.now();
const reduced = () => matchMedia("(prefers-reduced-motion: reduce)").matches;
const delay = (ms, signal) => new Promise((resolve, reject) => {
    if (signal.aborted) { reject(new DOMException("Cancelled", "AbortError")); return; }
    const onAbort = () => { clearTimeout(timer); reject(new DOMException("Cancelled", "AbortError")); };
    const timer = setTimeout(() => { signal.removeEventListener("abort", onAbort); resolve(); }, ms);
    signal.addEventListener("abort", onAbort, { once: true });
});

// Plays a short animation that also completes where the page timeline is throttled or frozen.
function play(element, keyframes, duration, easing = "ease-out") {
    if (typeof element.animate !== "function") return Promise.resolve();
    const animation = element.animate(keyframes, { duration, easing });
    const fallback = setTimeout(() => { try { animation.finish(); } catch { } }, duration + 150);
    return animation.finished.catch(() => { }).finally(() => clearTimeout(fallback));
}

function fadeOut(element, keyframes = [{ opacity: 1 }, { opacity: 0 }], duration = 180) {
    element.classList.add("is-leaving");
    element.setAttribute("aria-hidden", "true");
    element.style.opacity = "0";
    void play(element, keyframes, duration, "ease-in").finally(() => element.remove());
}

function target(request) {
    const id = request.target;
    if (id === "chat.fork") return document.querySelector('button[aria-label="Fork from here"]:not(:disabled)');
    if (id === "chat.edit_branch") return document.querySelector('button[aria-label="Edit and branch"]');
    if (id === "branch") return document.querySelector(`.branch-nav-row[data-branch-id="${CSS.escape(request.branchId || "")}"] .branch-link`);
    if (id === "chat") return document.querySelector(`.chat-nav-row[data-chat-id="${CSS.escape(request.chatId || "")}"] .chat-link`);
    if (id === "project") return document.querySelector(`.project-nav-row[data-project-id="${CSS.escape(request.projectId)}"] .project-link`);
    return document.querySelector(`[data-app-target="${CSS.escape(id)}"]`);
}

function control(element) {
    return element.matches("button,input,textarea,select,[contenteditable=true],a") ? element
        : element.querySelector("textarea,input,select,[contenteditable=true]") || element.querySelector("button,a") || element;
}

// A large widget can be taller than its scroll pane. Point at the visible part, not an
// off-screen centre, and scroll only the widget column rather than the conversation.
function guideRect(element) {
    const rect = element.getBoundingClientRect();
    const pane = element.closest?.(".chat-widgets-list");
    if (!pane) return rect;
    const bounds = pane.getBoundingClientRect();
    const left = Math.max(0, rect.left, bounds.left), right = Math.min(innerWidth, rect.right, bounds.right);
    const top = Math.max(0, rect.top, bounds.top), bottom = Math.min(innerHeight, rect.bottom, bounds.bottom);
    return { left, right, top, bottom, width: Math.max(0, right - left), height: Math.max(0, bottom - top) };
}

export function attach(reference) {
    const activity = event => {
        if (!event.isTrusted) return;
        lastActivity = Date.now();
        if (event.type === "pointerdown" || event.type === "pointermove") pointerPosition = { x: event.clientX, y: event.clientY };
    };
    const reposition = () => { if (positionedRequest) position(positionedRequest); };
    window.addEventListener("resize", reposition);
    window.addEventListener("scroll", reposition, true);
    for (const type of ["pointerdown", "pointermove", "keydown", "input", "wheel", "touchstart"]) window.addEventListener(type, activity, { capture: true, passive: true });
    return { dispose() {
        clear();
        window.removeEventListener("resize", reposition);
        window.removeEventListener("scroll", reposition, true);
        for (const type of ["pointerdown", "pointermove", "keydown", "input", "wheel", "touchstart"]) window.removeEventListener(type, activity, true);
    } };
}

export function idleSeconds() { return document.visibilityState === "visible" && document.hasFocus() ? (Date.now() - lastActivity) / 1000 : 0; }
export function language() { return navigator.languages?.[0] || navigator.language || null; }

export function tryOffer() {
    try {
        const preferences = JSON.parse(localStorage.getItem("ai-client.settings") || "{}");
        const saved = Date.parse(preferences.guideLastOfferedAt || "") || 0;
        const lease = Number(localStorage.getItem("ai-client.guide-offer-lease") || 0);
        if (preferences.guideSuggestionsEnabled === false || Date.now() - Math.max(saved, lease) < 86400000) return false;
        localStorage.setItem("ai-client.guide-offer-lease", String(Date.now()));
        return true;
    } catch { return false; }
}

export function clear(keepCursor = false) {
    active?.abort();
    active = null;
    positionedRequest = null;
    pointedRequest = null;
    layoutObserver?.disconnect();
    layoutObserver = null;
    cancelCursor();
    document.querySelectorAll(".app-guide-hover").forEach(element => {
        element.dispatchEvent(new PointerEvent("pointerleave", { bubbles: false }));
        element.dispatchEvent(new MouseEvent("mouseleave", { bubbles: false }));
    });
    document.querySelectorAll(".app-guide-target,.app-guide-hover").forEach(element => element.classList.remove("app-guide-target", "app-guide-hover"));
    document.querySelectorAll(keepCursor ? ".app-guide-tooltip:not(.is-leaving)" : ".app-guide-cursor:not(.is-leaving),.app-guide-tooltip:not(.is-leaving)")
        .forEach(element => fadeOut(element));
}

// The step card is Blazor's; a detached copy fades out while Blazor removes the original.
export function dismiss() {
    const card = document.querySelector(".app-guide-step[data-placed]");
    if (card) {
        const ghost = card.cloneNode(true);
        ghost.removeAttribute("data-placed");
        ghost.removeAttribute("role");
        ghost.removeAttribute("aria-live");
        ghost.inert = true;
        document.body.appendChild(ghost);
        fadeOut(ghost, [{ opacity: 1, transform: "none" }, { opacity: 0, transform: "translateY(6px) scale(.98)" }], 200);
    }
    clear();
}

export function available(request) {
    const element = target(request);
    return !!element && element.getClientRects().length > 0
        && (["show", "hover"].includes(request.action) || !control(element).disabled);
}

export async function waitForTarget(request) {
    for (let attempt = 0; attempt < 40; attempt++) {
        // A guided field can be inside a closed disclosure. Reveal it without changing its value.
        for (let ancestor = target(request)?.parentElement; ancestor; ancestor = ancestor.parentElement)
            if (ancestor.tagName === "DETAILS") ancestor.open = true;
        if (available(request)) return true;
        await new Promise(resolve => setTimeout(resolve, 50));
    }
    return false;
}

export function cancelAnimation() {
    clear();
}

export async function show(request) {
    // The pointer stays for the next perform to carry it on from where it is.
    clear(true);
    const element = target(request);
    if (!element || !element.getClientRects().length) return false;
    const controller = new AbortController();
    active = controller;
    if (typeof ResizeObserver === "function") {
        layoutObserver = new ResizeObserver(() => { if (positionedRequest) position(positionedRequest); });
        for (let ancestor = control(element); ancestor; ancestor = ancestor.parentElement) layoutObserver.observe(ancestor);
    }
    const pane = element.closest?.(".chat-widgets-list");
    const behavior = reduced() ? "instant" : "smooth";
    const scrollTop = pane ? Math.max(0, pane.scrollTop + element.getBoundingClientRect().top - pane.getBoundingClientRect().top - 8) : null;
    if (pane) pane.scrollTo({ top: scrollTop, behavior });
    else element.scrollIntoView({ block: "nearest", inline: "nearest", behavior });
    element.classList.add("app-guide-target");
    await delay(reduced() ? 0 : 350, controller.signal).catch(() => {});
    if (active === controller && pane) {
        const rect = guideRect(element);
        // Finish an interrupted/throttled smooth scroll before positioning the pointer and card.
        if (rect.height <= 0 || element.getBoundingClientRect().top < pane.getBoundingClientRect().top)
            pane.scrollTo({ top: scrollTop, behavior: "instant" });
    }
    return active === controller;
}

export function position(request) {
    positionedRequest = request;
    const element = target(request);
    const card = document.querySelector(".app-guide-step:not(.is-leaving)");
    if (!element) return;
    const inputRect = guideRect(control(element));
    if (pointedRequest) {
        const cursor = document.querySelector(".app-guide-cursor:not(.is-leaving)");
        if (cursor && inputRect.width > 0 && inputRect.height > 0) {
            cursorPosition = { x: inputRect.left + Math.min(28, inputRect.width / 2), y: inputRect.top + inputRect.height / 2 };
            cursor.style.transform = `translate(${cursorPosition.x}px, ${cursorPosition.y}px)`;
        }
    }
    if (!card) return;
    layoutObserver?.observe(card);
    const rect = guideRect(element);
    const box = card.getBoundingClientRect();
    const padding = 12, gap = 14;
    const fitsRight = rect.right + gap + box.width + padding <= innerWidth;
    const fitsLeft = rect.left - gap - box.width >= padding;
    const side = fitsRight || !fitsLeft ? "right" : "left";
    const left = side === "right" ? rect.right + gap : rect.left - gap - box.width;
    const clampedLeft = Math.max(padding, Math.min(left, innerWidth - box.width - padding));
    const top = Math.max(padding, Math.min(rect.top + rect.height / 2 - 28, innerHeight - box.height - padding));
    card.style.left = `${clampedLeft}px`;
    card.style.top = `${top}px`;
    // The arrow points at the control while the card sits beside it rather than over it.
    const beside = side === "right" ? clampedLeft >= rect.right : clampedLeft + box.width <= rect.left;
    card.dataset.side = beside ? side : "none";
    card.style.setProperty("--app-guide-arrow-y", `${Math.max(14, Math.min(rect.top + rect.height / 2 - top, box.height - 14))}px`);
    if (!card.hasAttribute("data-placed")) {
        card.setAttribute("data-placed", "");
        const from = side === "right" ? "translateX(-8px)" : "translateX(8px)";
        void play(card, [{ opacity: 0, transform: `${from} scale(.98)` }, { opacity: 1, transform: "none" }], 240,
            "cubic-bezier(0.2, 0, 0, 1)");
    }
}

export function watchTarget(request, reference) {
    const element = target(request);
    if (!element || !active) return;
    const signal = active.signal;
    const onUsed = event => {
        if (event.isTrusted) void reference.invokeMethodAsync("OnGuideTargetUsed", request.requestId);
    };
    for (const type of ["click", "input", "change"]) element.addEventListener(type, onUsed, { signal });
}

// A user-clicked shortcut only reveals its destination; it does not start or alter a guide.
export async function revealNavigationTarget(request, focus = false) {
    if (!await waitForTarget(request)) return false;
    const element = target(request);
    element?.scrollIntoView({ block: "center", behavior: "smooth" });
    if (focus) element?.focus({ preventScroll: true });
    return true;
}

export async function perform(request, activate = true) {
    const element = target(request);
    if (!element || !active || !element.getClientRects().length) return "Target is unavailable.";
    const signal = active.signal;
    const input = control(element);
    if (input.disabled && !["show", "hover"].includes(request.action)) return "This control is disabled.";
    // A pointer already on screen hands over in place, so a Continue does not blink it.
    const previous = document.querySelectorAll(".app-guide-cursor:not(.is-leaving)");
    previous.forEach(item => item.remove());
    const rect = guideRect(input);
    const cursor = createCursor("app-guide-cursor");
    const x = rect.left + Math.min(28, rect.width / 2), y = rect.top + rect.height / 2;
    const origin = cursorPosition || pointerPosition || { x: innerWidth / 2, y: innerHeight / 2 };
    const from = { x: Math.max(0, Math.min(origin.x, innerWidth - 26)), y: Math.max(0, Math.min(origin.y, innerHeight - 26)) };
    const distance = Math.hypot(x - from.x, y - from.y);
    // A slight arc reads as a hand moving the mouse rather than a straight slide.
    const bend = Math.min(60, distance * .12);
    const mid = { x: (from.x + x) / 2 + (distance ? (y - from.y) / distance * bend : 0), y: (from.y + y) / 2 - (distance ? (x - from.x) / distance * bend : 0) };
    const at = point => `translate(${point.x}px, ${point.y}px)`;
    cursor.style.transform = at({ x, y });
    document.body.appendChild(cursor);
    let completed = false;
    try {
        // The pointer always travels, even with reduced motion: where it lands only reads from its path.
        if (!previous.length) void play(cursor, [{ opacity: 0 }, { opacity: 1 }], 160);
        const duration = Math.round(Math.max(500, Math.min(1100, 380 + distance * .7)));
        const animation = typeof cursor.animate === "function"
            ? cursor.animate([{ transform: at(from) }, { transform: at(mid), offset: .5 }, { transform: at({ x, y }) }],
                { duration, easing: "cubic-bezier(0.45, 0, 0.2, 1)" })
            : null;
        if (animation) {
            const onAbort = () => animation.cancel();
            signal.addEventListener("abort", onAbort, { once: true });
            const fallback = setTimeout(() => { try { animation.finish(); } catch { } }, duration + 150);
            const stopTrail = startTrail(cursor);
            try {
                if (signal.aborted) animation.cancel();
                await animation.finished;
            } finally { stopTrail(); clearTimeout(fallback); signal.removeEventListener("abort", onAbort); }
        }
        await delay(200, signal);
        cursorPosition = { x, y };
        if (!element.isConnected) return "Target changed before the action.";
        if (request.expiresAt && Date.parse(request.expiresAt) <= Date.now()) return "Action expired.";
        if (request.action === "hover") {
            element.classList.add("app-guide-hover");
            element.dispatchEvent(new PointerEvent("pointerenter", { bubbles: false }));
            element.dispatchEvent(new MouseEvent("mouseenter", { bubbles: false }));
            const title = element.getAttribute("title") || input.getAttribute("title");
            if (title) {
                const tip = document.createElement("span");
                tip.className = "app-guide-tooltip"; tip.textContent = title;
                tip.style.left = `${Math.max(12, Math.min(rect.left, innerWidth - 280))}px`;
                tip.style.top = `${Math.max(12, Math.min(rect.bottom + 8, innerHeight - 80))}px`;
                document.body.appendChild(tip);
                void play(tip, [{ opacity: 0, transform: "translateY(-4px)" }, { opacity: 1, transform: "none" }], 180);
            }
        } else if (request.action === "click") {
            pressCursor(cursor, element);
            await delay(200, signal);
            if (activate) input.click();
            await delay(450, signal);
        } else if (request.action === "focus") input.focus();
        else if (request.action === "set_value") {
            input.focus();
            if (input.type === "checkbox") {
                if (!["true", "false"].includes(request.value)) return "A switch value must be true or false.";
                if (input.checked !== (request.value === "true")) input.click();
            } else if (element.getAttribute("role") === "radiogroup") {
                const radio = Array.from(element.querySelectorAll("input[type=radio]")).find(item => item.value === request.value);
                if (!radio) return "Unknown option value.";
                radio.click();
            } else if (input.isContentEditable) {
                input.textContent = request.value;
                input.dispatchEvent(new InputEvent("input", { bubbles: true, inputType: "insertText", data: request.value }));
            } else {
                const setter = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(input), "value")?.set;
                if (!setter) return "This control cannot accept a value.";
                setter.call(input, request.value);
                input.dispatchEvent(new Event("input", { bubbles: true }));
                input.dispatchEvent(new Event("change", { bubbles: true }));
            }
        }
        completed = true;
        pointedRequest = request;
        return null;
    } catch (error) {
        if (error.name === "AbortError") return "Action was stopped.";
        throw error;
    } finally { if (!completed) cursor.remove(); }
}

// When a timer runs out, the button whose action is about to be applied is pressed by the virtual
// pointer first, so the person sees what happened and why. Only the gesture is played here: the
// caller applies the action itself, exactly as it would without the pointer.
export async function press(button) {
    const element = typeof button === "string" ? document.querySelector(button) : button;
    if (!element?.isConnected || !element.getClientRects().length) return false;
    pointedRequest = null;
    element.scrollIntoView({ block: "nearest", inline: "nearest", behavior: "instant" });
    const rect = element.getBoundingClientRect();
    const x = rect.left + Math.min(rect.width / 2, 28), y = rect.top + rect.height / 2;
    // A pointer already on screen moves on from where it is instead of appearing again.
    let cursor = document.querySelector(".app-guide-cursor:not(.is-leaving)");
    const origin = cursor && cursorPosition || pointerPosition || { x: innerWidth / 2, y: innerHeight / 2 };
    const from = { x: Math.max(0, Math.min(origin.x, innerWidth - 26)), y: Math.max(0, Math.min(origin.y, innerHeight - 26)) };
    const at = point => `translate(${point.x}px, ${point.y}px)`;
    if (!cursor) {
        cursor = createCursor("app-guide-cursor");
        document.body.appendChild(cursor);
        void play(cursor, [{ opacity: 0 }, { opacity: 1 }], 160);
    }
    cursor.classList.remove("is-pressing", "is-released");
    cursor.style.transform = at({ x, y });
    const distance = Math.hypot(x - from.x, y - from.y);
    const stopTrail = startTrail(cursor);
    await play(cursor, [{ transform: at(from) }, { transform: at({ x, y }) }],
        Math.round(Math.max(350, Math.min(750, 250 + distance * .5))), "cubic-bezier(0.45, 0, 0.2, 1)");
    stopTrail();
    cursorPosition = { x, y };
    pressCursor(cursor, element);
    element.classList.add("is-auto-pressed");
    await new Promise(resolve => setTimeout(resolve, 250));
    // The pointer leaves on its own a moment after the press; the guide's next step brings its own.
    const pressed = cursor;
    setTimeout(() => { element.classList.remove("is-auto-pressed"); if (pressed.isConnected && !pressed.classList.contains("is-leaving")) fadeOut(pressed); }, 600);
    return true;
}
