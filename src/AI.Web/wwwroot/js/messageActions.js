(() => {
    // Copy gives no other sign that it worked: the icon turns into a check for a moment.
    const copiedFor = 1200;
    document.addEventListener("click", event => {
        const button = event.target instanceof Element ? event.target.closest(".message-copy-action") : null;
        if (!button) return;
        button.classList.add("is-copied");
        clearTimeout(button.copiedTimer);
        button.copiedTimer = setTimeout(() => button.classList.remove("is-copied"), copiedFor);
    });

    // A message's "More" menu is a popover, so it lives in the top layer and is never clipped by
    // the feed's scroller or the bubble's containment — but the top layer centres it on the
    // viewport. Pin it to its button instead: under it, right edges aligned, and above it when the
    // button sits too close to the bottom for the menu to fit.
    const gap = 4;
    const margin = 8;

    function invokerOf(menu) {
        return document.querySelector(`.message-more-action[popovertarget="${CSS.escape(menu.id)}"]`);
    }

    function place(menu) {
        const invoker = invokerOf(menu);
        if (!invoker) return;
        const anchor = invoker.getBoundingClientRect();
        const width = menu.offsetWidth;
        const height = menu.offsetHeight;
        const left = Math.max(margin, Math.min(anchor.right - width, window.innerWidth - width - margin));
        const below = anchor.bottom + gap + height <= window.innerHeight - margin;
        const top = below ? anchor.bottom + gap : Math.max(margin, anchor.top - gap - height);
        menu.style.left = `${left}px`;
        menu.style.top = `${top}px`;
    }

    // toggle does not bubble; capture still sees it. It fires after the menu is shown, when its
    // size is known, and before the next paint.
    document.addEventListener("toggle", event => {
        const menu = event.target;
        if (!(menu instanceof HTMLElement) || !menu.classList.contains("message-more-menu")) return;
        if (event.newState === "open") {
            place(menu);
            invokerOf(menu)?.classList.add("menu-open");
            // The menu itself, not its first item: when Fork is disabled that item is the
            // destructive one, and a stray Enter would start replacing the branch.
            menu.focus({ preventScroll: true });
        } else {
            invokerOf(menu)?.classList.remove("menu-open");
        }
    }, true);

    // Fixed coordinates do not follow the feed: keep the menu on its button while it scrolls
    // (a streaming answer scrolls the feed on its own) or the window resizes.
    const follow = () => {
        for (const menu of document.querySelectorAll(".message-more-menu:popover-open")) place(menu);
    };
    document.addEventListener("scroll", follow, { capture: true, passive: true });
    window.addEventListener("resize", follow, { passive: true });

    // Arrow keys walk the menu's items, as in any menu.
    document.addEventListener("keydown", event => {
        if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;
        const menu = event.target instanceof Element ? event.target.closest(".message-more-menu") : null;
        if (!menu) return;
        const items = [...menu.querySelectorAll("button:not(:disabled)")];
        if (items.length === 0) return;
        event.preventDefault();
        const index = items.indexOf(document.activeElement);
        const down = event.key === "ArrowDown";
        const next = index < 0 ? (down ? 0 : items.length - 1) : (index + (down ? 1 : -1) + items.length) % items.length;
        items[next].focus();
    });
})();
