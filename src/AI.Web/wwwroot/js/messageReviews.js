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

    // A comment is drawn as spans around its text rather than as a CSS highlight: a highlight can
    // set only colours and the underline, not the rounded corners .file-link has. The spans hold
    // the same text nodes, so the text offsets comments are anchored by do not move.
    const blockParents = new Set(["TABLE", "THEAD", "TBODY", "TFOOT", "TR", "UL", "OL", "DL"]);

    function unwrapMarks() {
        for (const mark of document.querySelectorAll(".review-comment-mark")) mark.replaceWith(...mark.childNodes);
    }

    function wrapRange(container, range, commentId) {
        const root = range.commonAncestorContainer;
        const pieces = [];
        if (root.nodeType === Node.TEXT_NODE) {
            pieces.push([root, range.startOffset, range.endOffset]);
        } else {
            const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
            let node;
            while ((node = walker.nextNode())) {
                if (!range.intersectsNode(node)) continue;
                // Layout whitespace between table rows or list items is not text of the comment.
                if (!node.textContent.trim() && blockParents.has(node.parentNode.nodeName)) continue;
                const start = node === range.startContainer ? range.startOffset : 0;
                const end = node === range.endContainer ? range.endOffset : node.textContent.length;
                if (start < end) pieces.push([node, start, end]);
            }
        }
        // Split only once every piece is known: splitting moves the range's own boundaries.
        const covered = new Set();
        for (const [node, start, end] of pieces) {
            if (end < node.textContent.length) node.splitText(end);
            covered.add(start > 0 ? node.splitText(start) : node);
        }
        // Climb to the outermost inline element the comment covers whole, and give neighbours one
        // span, so bold or a link inside a comment does not cut the mark into separately rounded bits.
        const coversWhole = element => {
            const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
            let node;
            while ((node = walker.nextNode())) if (node.textContent && !covered.has(node)) return false;
            return true;
        };
        const tops = [];
        for (const text of covered) {
            let top = text;
            while (top.parentNode !== container && top.parentNode instanceof HTMLElement
                && getComputedStyle(top.parentNode).display === "inline" && coversWhole(top.parentNode))
                top = top.parentNode;
            if (tops.at(-1) !== top) tops.push(top);
        }
        const marks = [];
        for (const top of tops) {
            const last = marks.at(-1);
            if (last && last.nextSibling === top) {
                last.appendChild(top);
                continue;
            }
            const mark = document.createElement("span");
            mark.className = "review-comment-mark";
            if (commentId) mark.dataset.reviewCommentId = commentId;
            top.before(mark);
            mark.appendChild(top);
            marks.push(mark);
        }
        if (marks.length > 0) {
            range.setStartBefore(marks[0]);
            range.setEndAfter(marks.at(-1));
        }
        return marks;
    }

    function rebuild() {
        scheduled = false;
        // The comment on show may be gone or moved; the next pointer move shows what is there now.
        hideTooltip();
        drawnByContainer.clear();
        unwrapMarks();
        for (const [messageId, comments] of commentsByMessage) {
            const container = containerFor(messageId);
            if (!container) continue;
            const own = [];
            for (const comment of comments) {
                const range = offsetRange(container, comment.start, comment.end);
                if (range && range.toString() === comment.quote) own.push({ range, id: comment.id, body: comment.body ?? "" });
            }
            own.sort((left, right) => left.range.compareBoundaryPoints(Range.END_TO_END, right.range));
            for (const item of own) item.marks = wrapRange(container, item.range, item.id);
            placeMarkers(container, own.map(item => item.range));
            if (own.length > 0) drawnByContainer.set(container, own);
        }
    }

    function schedule() {
        if (scheduled) return;
        scheduled = true;
        requestAnimationFrame(rebuild);
    }

    // The pointer is tested against the rectangles of each comment's marks, at most every 50 ms,
    // and the app's one tooltip (tooltips.js) shows every comment under it, which one title per
    // mark could not. Not the range's rectangles: the range is live, and when fileLinks.js
    // later replaces the text node just before a mark with a path link, the range's start stays
    // ahead of the new nodes and the comment grows over the path. Comments are few per message,
    // so this is a handful of rectangle checks.
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
            for (const rect of item.marks.flatMap(mark => [...mark.getClientRects()])) {
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

    // Pointing at a comment in a message's comment list brightens its mark in the text, so the
    // user sees which fragment it is about without opening it.
    function emphasize(commentId) {
        for (const mark of document.querySelectorAll(".review-comment-mark.is-emphasized")) mark.classList.remove("is-emphasized");
        if (!commentId) return;
        for (const mark of document.querySelectorAll(`.review-comment-mark[data-review-comment-id="${CSS.escape(commentId)}"]`))
            mark.classList.add("is-emphasized");
    }

    for (const type of ["pointerover", "focusin"]) {
        document.addEventListener(type, event => {
            const row = event.target instanceof Element ? event.target.closest("[data-review-comment-id]") : null;
            if (row?.closest(".message-review-comments")) emphasize(row.dataset.reviewCommentId);
        }, { passive: true });
    }
    for (const type of ["pointerout", "focusout"]) {
        document.addEventListener(type, event => {
            const row = event.target instanceof Element ? event.target.closest(".message-review-comments [data-review-comment-id]") : null;
            const next = event.relatedTarget instanceof Element ? event.relatedTarget.closest(".message-review-comments [data-review-comment-id]") : null;
            if (row && row !== next) emphasize(next?.dataset.reviewCommentId ?? null);
        }, { passive: true });
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
        // Half the editor's width (22rem in app.css), so it stays inside the window.
        const halfWidth = Math.min(176, Math.max(0, window.innerWidth / 2 - 12));
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

    // Tab or → in an empty comment box takes the Host's draft shown in it (ReviewCommentInput) as
    // typed text. Decided here because only a synchronous handler can keep Tab from moving focus.
    document.addEventListener("keydown", event => {
        const textarea = event.target;
        if (!(textarea instanceof HTMLTextAreaElement) || !textarea.dataset.suggestion || textarea.value.length > 0) return;
        if (event.key !== "Tab" && event.key !== "ArrowRight") return;
        if (event.ctrlKey || event.altKey || event.shiftKey || event.metaKey || event.isComposing) return;
        event.preventDefault();
        const text = textarea.dataset.suggestion;
        textarea.value = text;
        textarea.setSelectionRange(text.length, text.length);
        textarea.dispatchEvent(new Event("input", { bubbles: true }));
    }, true);

    // The open comment editor. A new comment's editor does not take focus, which would drop the
    // selection the user may only have made to copy: copying closes it, typing moves into it, and
    // a click anywhere else saves what was typed and closes it.
    let active = null;

    function openEditor() {
        if (active && active.editor.isConnected && active.editor.matches(":popover-open")) return active;
        active = null;
        return null;
    }

    function dismiss(save) {
        const current = openEditor();
        if (!current) return;
        active = null;
        current.owner.invokeMethodAsync("DismissEditor", save).catch(() => { });
    }

    document.addEventListener("copy", event => {
        const current = openEditor();
        if (current && !(event.target instanceof Node && current.editor.contains(event.target))) dismiss(false);
    }, true);

    // Only the main button: a right click opens the menu the user may copy from.
    document.addEventListener("pointerdown", event => {
        const current = openEditor();
        if (current && event.button === 0 && !(event.target instanceof Node && current.editor.contains(event.target))) dismiss(true);
    }, true);

    document.addEventListener("keydown", event => {
        const current = openEditor();
        if (!current || (event.target instanceof Node && current.editor.contains(event.target))) return;
        if (event.key === "Escape") {
            event.preventDefault();
            dismiss(false);
            return;
        }
        const target = event.target instanceof Element ? event.target : null;
        const typing = target?.closest("input, textarea, select, [contenteditable]:not([contenteditable=false])");
        // Focusing during keydown sends the character to the comment text.
        if (!typing && event.key.length === 1 && !event.ctrlKey && !event.altKey && !event.metaKey && !event.isComposing)
            current.editor.querySelector("textarea")?.focus();
    }, true);

    window.messageReviews = {
        showEditor(messageId, owner, focus) {
            const editor = document.getElementById(`review-editor-${messageId}`);
            if (!editor) return;
            if (!editor.matches(":popover-open")) editor.showPopover();
            active = { editor, owner };
            if (focus) editor.querySelector("textarea")?.focus();
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
