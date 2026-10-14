// The running turn's notes window (MessageFeed.razor, .live-notes; docs/18-compact-turn-view.md).
// Blazor renders the notes; this script only measures them. It keeps the window as tall as the
// newest notes, at most two lines for each note the settings keep and never lower than it has been
// during the turn, and glides the track up whenever text arrives below what is shown, so the reader
// sees one movement, always upwards, and nothing around the window moves with it.
(() => {
    const LinesPerNote = 2;

    const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)");

    const resizeObserver = new ResizeObserver(entries => {
        for (const entry of entries) {
            const root = entry.target.closest(".live-notes");
            if (root?.liveNotes) update(root);
        }
    });

    // Two lines per kept note and the gaps between them, measured from the notes themselves so the
    // cap follows the message font size the person chose.
    const capOf = (track, limit) => {
        const text = track.querySelector(".live-text:not(.is-older) .markdown-content");
        if (!text) return 0;
        const style = getComputedStyle(text);
        const line = parseFloat(style.lineHeight) || parseFloat(style.fontSize) * 1.5;
        const gap = track.children.length > 1 ? parseFloat(getComputedStyle(track.firstElementChild).marginBottom) || 0 : 0;
        return limit * LinesPerNote * line + (limit - 1) * gap;
    };

    // Puts the track back where its text was before it grew by `grown`, without motion, then lets
    // it glide to rest. The window grows on the same curve, so text already shown stays still.
    const glide = (track, grown) => {
        const current = new DOMMatrixReadOnly(getComputedStyle(track).transform).m42;
        track.classList.add("is-snapping");
        track.style.transform = `translateY(${current + grown}px)`;
        void track.offsetHeight;
        track.classList.remove("is-snapping");
        track.style.transform = "translateY(0)";
    };

    const update = root => {
        const state = root.liveNotes;
        const { track, view } = state;
        if (!track.isConnected) {
            resizeObserver.unobserve(track);
            return;
        }

        const trackHeight = track.offsetHeight;
        const firstKept = track.querySelector(".live-text:not(.is-older)");
        const kept = firstKept ? trackHeight - firstKept.offsetTop : 0;
        const limit = Math.max(1, Number(root.dataset.liveLimit) || 1);
        // Never lower during the turn, except when the person keeps fewer notes than before.
        state.height = Math.min(Math.max(state.height, kept), capOf(track, limit) || kept);
        view.style.setProperty("--live-notes-height", `${state.height}px`);
        // The top dissolves only where the window's edge cuts through a note, not when a note
        // starts right at it.
        const edge = trackHeight - state.height;
        view.classList.toggle("is-overflowing", edge > 1
            && ![...track.children].some(note => Math.abs(note.offsetTop - edge) <= 1));

        // What arrived below the note that was newest last time: that note's own growth and any
        // note after it. A note dropped above changes nothing, the track being pinned to the bottom.
        const anchor = state.anchor?.parentElement === track ? state.anchor : null;
        const grown = anchor ? trackHeight - anchor.offsetTop - state.anchorFromBottom : trackHeight - state.trackHeight;
        const last = track.lastElementChild;
        state.anchor = last;
        state.anchorFromBottom = last ? trackHeight - last.offsetTop : 0;
        state.trackHeight = trackHeight;
        if (grown > 0.5 && !reducedMotion.matches) glide(track, grown);
    };

    const attach = root => {
        if (root.liveNotes) return;
        const track = root.querySelector(".live-notes-track");
        const view = root.querySelector(".live-notes-window");
        if (!track || !view) return;
        root.liveNotes = { track, view, height: 0, trackHeight: 0, anchor: null, anchorFromBottom: 0 };
        update(root);
        resizeObserver.observe(track);
    };

    const visit = node => {
        if (!(node instanceof Element)) return;
        if (node.matches(".live-notes")) attach(node);
        for (const root of node.querySelectorAll(".live-notes")) attach(root);
    };

    const start = () => {
        visit(document.body);
        new MutationObserver(records => {
            for (const record of records) {
                if (record.type === "childList") {
                    for (const node of record.addedNodes) visit(node);
                } else if (record.target.liveNotes) {
                    update(record.target);
                }
            }
        }).observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ["data-live-limit"] });
    };

    if (document.body) start();
    else document.addEventListener("DOMContentLoaded", start, { once: true });
})();
