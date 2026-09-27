(() => {
    const commentsByMessage = new Map();
    // The ranges drawn last time, with their comment text, for the hover tooltip.
    const drawnByContainer = new Map();
    let scheduled = false;

    function containerFor(messageId) {
        return document.getElementById(`review-message-${messageId}`);
    }

    function offsetRange(container, start, end) {
        const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
        let node;
        let offset = 0;
        let first = null;
        let last = null;
        while ((node = walker.nextNode())) {
            const next = offset + node.textContent.length;
            if (!first && start >= offset && start < next) first = [node, start - offset];
            if (end > offset && end <= next) { last = [node, end - offset]; break; }
            offset = next;
        }
        if (!first || !last) return null;
        const range = document.createRange();
        range.setStart(...first);
        range.setEnd(...last);
        return range;
    }

    // AppIcon "message-circle", the icon the edit statistics count comments with. Kept as markup
    // rather than redrawn so the two cannot drift apart.
    const commentIcon = '<svg class="app-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" '
        + 'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">'
        + '<path d="M21 11.5a8.5 8.5 0 0 1-8.5 8.5 8.4 8.4 0 0 1-3.9-.94L3 20l1.06-3.98A8.5 8.5 0 1 1 21 11.5Z" /></svg>';

    // A highlight cannot draw an icon, so each comment gets a marker element at its end. It holds
    // no text, so the text offsets comments are anchored by do not move.
    function placeMarkers(container, ranges) {
        for (const marker of container.querySelectorAll(".review-comment-marker")) marker.remove();
        // Last first: inserting splits a text node, and the ranges still to come stay ahead of it.
        for (const range of [...ranges].reverse()) {
            const end = range.cloneRange();
            end.collapse(false);
            const marker = document.createElement("span");
            marker.className = "review-comment-marker";
            marker.setAttribute("aria-hidden", "true");
            marker.innerHTML = commentIcon;
            end.insertNode(marker);
        }
    }

    function rebuild() {
        scheduled = false;
        if (!CSS.highlights) return;
        const ranges = [];
        // The comment on show may be gone or moved; the next pointer move shows what is there now.
        hideTooltip();
        drawnByContainer.clear();
        for (const [messageId, comments] of commentsByMessage) {
            const container = containerFor(messageId);
            if (!container) continue;
            const own = [];
            for (const comment of comments) {
                const range = offsetRange(container, comment.start, comment.end);
                if (range && range.toString() === comment.quote) own.push({ range, body: comment.body ?? "" });
            }
            own.sort((left, right) => left.range.compareBoundaryPoints(Range.END_TO_END, right.range));
            placeMarkers(container, own.map(item => item.range));
            if (own.length > 0) drawnByContainer.set(container, own);
            ranges.push(...own.map(item => item.range));
        }
        CSS.highlights.set("message-review", new Highlight(...ranges));
    }

    function schedule() {
        if (scheduled) return;
        scheduled = true;
        requestAnimationFrame(rebuild);
    }

    // A highlight is not an element, so it cannot carry a title: the pointer is tested against
    // the drawn ranges' own rectangles instead, at most every 50 ms, and the app's one tooltip
    // (tooltips.js) shows the comment. Comments are few per message, so this is a handful of
    // rectangle checks.
    let lastMove = 0;
    let shownFor = null;

    function hideTooltip() {
        if (shownFor === null) return;
        shownFor = null;
        window.appTooltip?.hide();
    }

    function hitAt(container, x, y) {
        const hits = [];
        let anchor = null;
        for (const item of drawnByContainer.get(container) ?? []) {
            for (const rect of item.range.getClientRects()) {
                if (x >= rect.left - 1 && x <= rect.right + 1 && y >= rect.top - 1 && y <= rect.bottom + 1) {
                    hits.push(item);
                    anchor ??= rect;
                    break;
                }
            }
        }
        return hits.length ? { hits, anchor } : null;
    }

    function showTooltip(hit, x) {
        const texts = hit.hits.map(item => item.body || "Empty comment");
        shownFor = texts.join("\n\n");
        window.appTooltip?.show(texts, hit.anchor, x);
    }

    document.addEventListener("pointermove", event => {
        if (drawnByContainer.size === 0) return;
        const now = performance.now();
        if (now - lastMove < 50) return;
        lastMove = now;
        const container = event.target instanceof Element ? event.target.closest('[id^="review-message-"]') : null;
        const hit = container ? hitAt(container, event.clientX, event.clientY) : null;
        if (hit) showTooltip(hit, event.clientX);
        else hideTooltip();
    }, { passive: true });
    // Clicks, scrolling and losing focus already hide the shared tooltip; this only forgets it.
    for (const type of ["pointerdown", "scroll"]) document.addEventListener(type, () => { shownFor = null; }, { capture: true, passive: true });

    function offsetAt(container, node, nodeOffset) {
        const range = document.createRange();
        range.selectNodeContents(container);
        range.setEnd(node, nodeOffset);
        return range.toString().length;
    }

    function positionFor(range, reveal = false) {
        let rect = range.getClientRects()[0] || range.getBoundingClientRect();
        if (reveal && (rect.top < 80 || rect.bottom > window.innerHeight - 80)) {
            range.startContainer.parentElement?.scrollIntoView({ block: "center" });
            rect = range.getClientRects()[0] || range.getBoundingClientRect();
        }
        const halfWidth = Math.min(136, Math.max(0, window.innerWidth / 2 - 12));
        const x = Math.max(halfWidth + 12, Math.min(rect.left + rect.width / 2, window.innerWidth - halfWidth - 12));
        const below = rect.top < 150;
        return { x, y: below ? rect.bottom + 8 : rect.top - 8, below };
    }

    // Enter saves a review comment (handled in Blazor), so keep it from inserting a newline; Shift+Enter still does.
    document.addEventListener("keydown", event => {
        if (event.key === "Enter" && !event.shiftKey && !event.ctrlKey && !event.altKey && !event.metaKey
            && !event.isComposing && event.target instanceof HTMLTextAreaElement
            && event.target.hasAttribute("data-review-comment"))
            event.preventDefault();
    }, true);

    window.messageReviews = {
        showEditor(messageId) {
            const editor = document.getElementById(`review-editor-${messageId}`);
            if (editor && !editor.matches(":popover-open")) {
                editor.showPopover();
                editor.querySelector("textarea")?.focus();
            }
        },
        setComments(messageId, comments) {
            commentsByMessage.set(messageId, comments || []);
            schedule();
        },
        capture(messageId, x, y) {
            const container = containerFor(messageId);
            if (!container) return null;
            const selection = window.getSelection();
            if (selection && !selection.isCollapsed && selection.rangeCount) {
                const range = selection.getRangeAt(0);
                if (container.contains(range.startContainer) && container.contains(range.endContainer)) {
                    const start = offsetAt(container, range.startContainer, range.startOffset);
                    const end = offsetAt(container, range.endContainer, range.endOffset);
                    const quote = range.toString();
                    if (start < end && quote && end - start <= 2000)
                        return { start, end, quote, hit: null, ...positionFor(range) };
                }
            }
            const caret = document.caretRangeFromPoint?.(x, y);
            if (caret && container.contains(caret.startContainer))
                return { start: 0, end: 0, quote: "", hit: offsetAt(container, caret.startContainer, caret.startOffset), x, y, below: false };
            return null;
        },
        locate(messageId, start, end) {
            const container = containerFor(messageId);
            if (!container) return null;
            const range = offsetRange(container, start, end);
            if (!range) return null;
            return { start, end, quote: range.toString(), hit: null, ...positionFor(range, true) };
        }
    };
})();
