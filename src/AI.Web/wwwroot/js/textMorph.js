// Text that changes in place without jumping. The part the old and the new text share stays where
// it is; the rest dissolves into the new text while its width eases to fit, so whatever follows on
// the line glides along instead of jumping. Used for the running turn's "Working for …" (each tick
// morphs only the digits that change) and for the activity line under it (docs/18-compact-turn-view.md).
//
// Blazor renders these elements empty and puts the text in attributes, so this script owns their
// children and the renderer never diffs them:
//   [data-morph]                  the element shows that text, morphing on every change;
//   .turn-activity[data-activity-*] the line's title, count and detail, held for a moment each.
(() => {
    // Matches the transitions of .text-morph-tail in app.css; the layers are folded back into plain
    // text once they have played, so the resting DOM is just the text again.
    const MorphMs = 260;
    // The activity line shows each state at least this long, so calls that start and finish within
    // a few frames do not flicker past; only the latest one waiting is shown when the time is up.
    const MinShownMs = 600;
    // "Thinking…" appears only after a pause this long: between two calls in quick succession it
    // would otherwise flash for a frame.
    const ThinkingAfterMs = 400;

    // A zero-width space keeps an empty layer one line tall, so the baseline never moves.
    const layer = (text, className) => {
        const span = document.createElement("span");
        span.className = className;
        span.textContent = text || "​";
        return span;
    };

    const sharedPrefix = (from, to) => {
        let length = 0;
        while (length < from.length && length < to.length && from[length] === to[length]) length++;
        // Never split a surrogate pair between the kept prefix and the morphing tail.
        if (length > 0 && /[\uD800-\uDBFF]/.test(to[length - 1])) length--;
        return length;
    };

    const morph = (element, text) => {
        const previous = element.morphText;
        if (previous === text) return;
        element.morphText = text;
        clearTimeout(element.morphTimer);
        if (previous === undefined || !element.isConnected) {
            element.textContent = text;
            return;
        }

        const kept = sharedPrefix(previous, text);
        const prefix = document.createElement("span");
        prefix.textContent = text.slice(0, kept);
        const tail = document.createElement("span");
        tail.className = "text-morph-tail";
        const leaving = layer(previous.slice(kept), "text-morph-layer");
        leaving.setAttribute("aria-hidden", "true");
        const arriving = layer(text.slice(kept), "text-morph-layer is-arriving");
        tail.append(leaving, arriving);
        element.replaceChildren(prefix, tail);

        // Inline width is runtime geometry: from the old tail's width to the new one's.
        tail.style.width = `${leaving.getBoundingClientRect().width}px`;
        const target = text.slice(kept) ? arriving.getBoundingClientRect().width : 0;
        tail.getBoundingClientRect();
        requestAnimationFrame(() => {
            tail.style.width = `${target}px`;
            leaving.classList.add("is-leaving");
            arriving.classList.remove("is-arriving");
        });
        element.morphTimer = setTimeout(() => { element.textContent = text; }, MorphMs);
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
        if (node.matches("[data-morph]")) morph(node, node.dataset.morph);
        if (node.matches(".turn-activity[data-activity-kind]")) updateActivity(node);
        for (const element of node.querySelectorAll("[data-morph]")) morph(element, element.dataset.morph);
        for (const line of node.querySelectorAll(".turn-activity[data-activity-kind]")) updateActivity(line);
    };

    const start = () => {
        visit(document.body);
        new MutationObserver(records => {
            const lines = new Set();
            for (const record of records) {
                if (record.type === "childList") {
                    for (const node of record.addedNodes) visit(node);
                } else if (record.attributeName === "data-morph") {
                    morph(record.target, record.target.dataset.morph);
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
            attributeFilter: ["data-morph", "data-activity-kind", "data-activity-title", "data-activity-count", "data-activity-detail"],
        });
    };

    if (document.body) start();
    else document.addEventListener("DOMContentLoaded", start, { once: true });
})();
