// Text that changes in place without jumping. The words the old and the new text share stay where
// they are; the rest decodes into the new text: each changed character runs through random letters
// and settles, roughly from left to right, while the width eases to fit, so whatever follows on the
// line glides along instead of jumping. Used for the activity line under the running turn
// (docs/18-compact-turn-view.md).
//
// Blazor renders the line empty and puts its state in .turn-activity[data-activity-*] attributes
// (the title, count and detail, held for a moment each), so this script owns its children and the
// renderer never diffs them.
(() => {
    // How long a change takes to settle, and how often the unsettled characters pick new letters.
    const DecodeMs = 300;
    const GlyphMs = 35;
    const Glyphs = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    // The activity line shows each state at least this long, so calls that start and finish within
    // a few frames do not flicker past; only the latest one waiting is shown when the time is up.
    const MinShownMs = 600;
    // "Thinking…" appears only after a pause this long: between two calls in quick succession it
    // would otherwise flash for a frame.
    const ThinkingAfterMs = 400;

    const reducedMotion = () => matchMedia("(prefers-reduced-motion: reduce)").matches
        || document.documentElement.dataset.animations === "false";

    // The shared start, cut back to a word boundary, so a word is never half kept and half decoded.
    const sharedWords = (from, to) => {
        let length = 0;
        while (length < from.length && length < to.length && from[length] === to[length]) length++;
        if (length < from.length || length < to.length) {
            while (length > 0 && to[length - 1] !== " ") length--;
        }
        return length;
    };

    // Spaces and separators keep their place, so the line keeps its shape while it decodes.
    const isFixed = ch => ch === " " || ch === "·" || ch === "…";

    const randomGlyph = () => Glyphs[Math.floor(Math.random() * Glyphs.length)];

    const stop = element => {
        clearTimeout(element.morphTimer);
        clearInterval(element.morphTicker);
    };

    const morph = (element, text) => {
        const previous = element.morphText;
        if (previous === text) return;
        element.morphText = text;
        stop(element);
        if (previous === undefined || !element.isConnected || reducedMotion()) {
            element.textContent = text;
            return;
        }

        const kept = sharedWords(previous, text);
        const from = [...previous.slice(kept)];
        const to = [...text.slice(kept)];
        const length = Math.max(from.length, to.length);
        // Each character settles at its own moment: mostly in reading order, with some spread so
        // the line does not read as a wipe.
        const settleAt = Array.from({ length }, (_, i) => (i / length * 0.65 + Math.random() * 0.35) * DecodeMs);

        const prefix = document.createElement("span");
        prefix.textContent = text.slice(0, kept);
        const tail = document.createElement("span");
        tail.className = "text-morph-tail";
        tail.setAttribute("aria-hidden", "true");
        element.replaceChildren(prefix, tail);

        // Inline width is runtime geometry: from the old tail's width to the new one's.
        tail.textContent = to.join("") || "​";
        const target = to.length ? tail.getBoundingClientRect().width : 0;
        tail.textContent = from.join("") || "​";
        tail.style.width = `${tail.getBoundingClientRect().width}px`;
        requestAnimationFrame(() => { tail.style.width = `${target}px`; });

        const started = performance.now();
        const frame = () => {
            const elapsed = performance.now() - started;
            let line = "";
            for (let i = 0; i < length; i++) {
                if (elapsed >= settleAt[i]) {
                    line += to[i] ?? "";
                    continue;
                }
                const ch = to[i] ?? from[i];
                line += isFixed(ch) ? ch : randomGlyph();
            }
            // A zero-width space keeps an empty tail one line tall, so the baseline never moves.
            tail.textContent = line || "​";
        };
        frame();
        element.morphTicker = setInterval(frame, GlyphMs);
        element.morphTimer = setTimeout(() => {
            stop(element);
            element.textContent = text;
        }, DecodeMs);
    };

    const readActivity = line => ({
        kind: line.dataset.activityKind || "idle",
        title: line.dataset.activityTitle || "",
        count: line.dataset.activityCount || "",
        detail: line.dataset.activityDetail || "",
    });

    const sameActivity = (a, b) => a && b && a.kind === b.kind && a.title === b.title && a.count === b.count && a.detail === b.detail;

    const showActivity = (line, state) => {
        const activity = line.activity;
        activity.shown = state;
        activity.shownAt = performance.now();
        const title = line.querySelector(".turn-activity-title");
        const count = line.querySelector(".turn-activity-count");
        const detail = line.querySelector(".turn-activity-detail");
        if (title) {
            morph(title, state.title);
            // The sheen means work in progress; a wait on the person is not that.
            title.classList.toggle("text-sheen", state.kind === "tool" || state.kind === "thinking");
        }
        if (count) morph(count, state.count);
        if (detail) {
            morph(detail, state.detail);
            if (state.detail) detail.title = state.detail;
            else detail.removeAttribute("title");
        }
    };

    // A tool call or a wait seen since the shown state was put up, and not the shown state itself.
    const pendingConcrete = activity => activity.concrete && activity.concrete.seenAt > activity.shownAt
        && !sameActivity(activity.shown, activity.concrete.state) ? activity.concrete.state : null;

    // When the hold is up: the latest state, unless that is a pause not yet long enough for
    // "Thinking…" (or "Thinking…" is already up) — then the latest call seen meanwhile, so a quick
    // series still shows itself and its count (Read file · 2, · 3) instead of vanishing in a pause.
    const nextActivity = (activity, wanted, now) => {
        if (wanted.kind !== "thinking") return wanted;
        const concrete = pendingConcrete(activity);
        if (concrete) return concrete;
        return activity.thinkingSince + ThinkingAfterMs <= now ? wanted : null;
    };

    const updateActivity = line => {
        const activity = line.activity ??= { shown: null, shownAt: 0, thinkingSince: 0, concrete: null, timer: 0 };
        const wanted = readActivity(line);
        const now = performance.now();
        if (wanted.kind !== "thinking") activity.thinkingSince = 0;
        else if (!activity.thinkingSince) activity.thinkingSince = now;
        if ((wanted.kind === "tool" || wanted.kind === "waiting") && !sameActivity(activity.concrete?.state, wanted))
            activity.concrete = { state: wanted, seenAt: now };

        clearTimeout(activity.timer);
        // Nothing shown yet: the line starts empty, so its first state needs no hold.
        const heldUntil = activity.shown && activity.shown.kind !== "idle" ? activity.shownAt + MinShownMs : 0;
        if (heldUntil <= now) {
            const next = nextActivity(activity, wanted, now);
            if (next && !sameActivity(activity.shown, next)) showActivity(line, next);
        }
        // Whatever is still not shown gets another look when it can be: the hold over, or the
        // pause long enough for "Thinking…".
        const concrete = pendingConcrete(activity);
        if (sameActivity(activity.shown, wanted) && !concrete) return;
        const held = activity.shown && activity.shown.kind !== "idle" ? activity.shownAt + MinShownMs : 0;
        const thinkingAt = wanted.kind === "thinking" && !concrete ? activity.thinkingSince + ThinkingAfterMs : 0;
        const due = Math.max(held, thinkingAt, now + 1);
        activity.timer = setTimeout(() => {
            if (line.isConnected) updateActivity(line);
        }, due - now);
    };

    const visit = node => {
        if (!(node instanceof Element)) return;
        if (node.matches(".turn-activity[data-activity-kind]")) updateActivity(node);
        for (const line of node.querySelectorAll(".turn-activity[data-activity-kind]")) updateActivity(line);
    };

    const start = () => {
        visit(document.body);
        new MutationObserver(records => {
            const lines = new Set();
            for (const record of records) {
                if (record.type === "childList") {
                    for (const node of record.addedNodes) visit(node);
                } else {
                    lines.add(record.target);
                }
            }
            // Blazor sets the line's attributes one by one; read them once, together.
            for (const line of lines) updateActivity(line);
        }).observe(document.body, {
            subtree: true,
            childList: true,
            attributes: true,
            attributeFilter: ["data-activity-kind", "data-activity-title", "data-activity-count", "data-activity-detail"],
        });
    };

    if (document.body) start();
    else document.addEventListener("DOMContentLoaded", start, { once: true });
})();
