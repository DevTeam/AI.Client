// Slides the right-hand drawers (settings, permissions, notifications, review) out when they close.
// They slide in on their own, by a CSS animation, but Blazor removes a closed drawer at once, and
// it closes in many ways (its button, Escape, a press outside, a switch of chat). So nothing in
// Blazor waits for the exit: a watcher takes the drawer Blazor just removed, puts it back over the
// page, inert and marked is-leaving, and drops it when its exit animation ends. The node is Blazor's
// no more, so putting it back does not touch what Blazor renders; it keeps its scroll position.
//
// A drawer that goes while another comes in the same render is a swap (a settings section with
// its own key), not a close: the old one goes at once and the new one does not slide in.
(() => {
    const DRAWER = ".permissions-drawer, .review-workspace, .archive-drawer";
    const BACKDROP = ".permissions-drawer-backdrop, .archive-backdrop";
    // After the longest exit animation, in case animationend never comes (a hidden page).
    const FALLBACK_MS = 400;
    let layer = null;

    function leavingLayer() {
        if (!layer?.isConnected) {
            layer = document.createElement("div");
            layer.className = "drawer-leaving-layer";
            layer.setAttribute("aria-hidden", "true");
            layer.inert = true;
            document.body.appendChild(layer);
        }
        return layer;
    }

    function reducedMotion() {
        return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    }

    function leave(node) {
        node.classList.add("is-leaving");
        leavingLayer().appendChild(node);
        let done = false;
        const drop = () => {
            if (done) return;
            done = true;
            node.remove();
        };
        node.addEventListener("animationend", event => { if (event.target === node) drop(); });
        setTimeout(drop, FALLBACK_MS);
    }

    function matching(nodes, selector) {
        const found = [];
        for (const node of nodes) {
            if (node instanceof Element && node.matches(selector)) found.push(node);
        }
        return found;
    }

    const observer = new MutationObserver(records => {
        const removed = [];
        const added = [];
        for (const record of records) {
            if (record.target === layer || layer?.contains(record.target)) continue;
            removed.push(...record.removedNodes);
            added.push(...record.addedNodes);
        }
        const gone = matching(removed, DRAWER).filter(node => !node.isConnected);
        const come = matching(added, DRAWER).filter(node => node.isConnected);
        if (come.length > 0 && gone.length > 0) {
            for (const node of come) node.classList.add("is-swapped");
            for (const node of matching(added, BACKDROP)) node.classList.add("is-swapped");
            return;
        }
        if (gone.length === 0 || reducedMotion()) return;
        for (const node of matching(removed, BACKDROP).filter(node => !node.isConnected)) leave(node);
        for (const node of gone) leave(node);
    });

    function start() {
        const app = document.getElementById("app");
        if (app) observer.observe(app, { childList: true, subtree: true });
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start, { once: true });
    else start();
})();
