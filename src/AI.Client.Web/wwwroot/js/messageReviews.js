(() => {
    const commentsByMessage = new Map();
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

    function rebuild() {
        scheduled = false;
        if (!CSS.highlights) return;
        const ranges = [];
        for (const [messageId, comments] of commentsByMessage) {
            const container = containerFor(messageId);
            if (!container) continue;
            for (const comment of comments) {
                const range = offsetRange(container, comment.start, comment.end);
                if (range && range.toString() === comment.quote) ranges.push(range);
            }
        }
        CSS.highlights.set("message-review", new Highlight(...ranges));
    }

    function schedule() {
        if (scheduled) return;
        scheduled = true;
        requestAnimationFrame(rebuild);
    }

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
        const halfWidth = Math.min(168, Math.max(0, window.innerWidth / 2 - 12));
        const x = Math.max(halfWidth + 12, Math.min(rect.left + rect.width / 2, window.innerWidth - halfWidth - 12));
        const below = rect.top < 190;
        return { x, y: below ? rect.bottom + 8 : rect.top - 8, below };
    }

    window.messageReviews = {
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
