// Sticky-bottom scrolling for the message feed, plus manual scroll anchoring.
//
// The feed used to jump to the bottom on every run snapshot, which made reading anything older
// than the last message impossible while an agent was working. The rule here instead: the feed
// follows new content only while the user is already at the bottom. Whether they are is decided
// from their own scrolling, recorded as it happens — by the time Blazor has rendered a new
// message the distance to the bottom already includes it, so it cannot be measured after the
// fact.

// How close to the bottom still counts as "at the bottom". A few pixels of slack are not enough:
// after a markdown block finishes laying out, the resting position is often tens of pixels off,
// and a stricter threshold silently drops the user out of follow mode.
const PinThresholdPx = 80;

const distanceFromBottom = scroller => scroller.scrollHeight - scroller.scrollTop - scroller.clientHeight;

// End belongs to the feed only when the user isn't typing — in a textarea it means "end of line".
const isTextEntry = target =>
    target instanceof HTMLElement
    && (target.isContentEditable || /^(input|textarea|select)$/i.test(target.tagName));

// Search highlighting. Walks every text node inside the scroller, wraps occurrences of the
// current query in <mark class="search-highlight"> and unwraps them when the query changes or
// the search closes. Applied again from Blazor after every render that touches the transcript,
// because Blazor's diff drops our <mark> wrappers along with everything else in the subtree.
const SearchHighlightClass = "search-highlight";

// The minimum length the sidebar applies before it sends anything to the server; mirroring it
// here means an empty/short query never wastes a DOM pass. The Blazor side does the same check
// before assigning the parameter, but doing it again on the JS side keeps this module honest on
// its own — a unit test that imports it directly does not have to know the sidebar's policy.
const MinSearchQueryLength = 2;

const escapeRegExp = text => text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

// Re-merge adjacent text nodes after unwrapping <mark>, otherwise a sequence like
// "foo<mark>bar</mark>baz" becomes three text nodes that a later TreeWalker sees separately and
// fragments further. normalize() restores the original single node so a re-apply starts clean.
const unwrapMark = mark => {
    const parent = mark.parentNode;
    if (parent === null) return;
    while (mark.firstChild) parent.insertBefore(mark.firstChild, mark);
    parent.removeChild(mark);
    parent.normalize();
};

const clearSearchHighlight = root => {
    const marks = root.querySelectorAll(`mark.${SearchHighlightClass}`);
    // A static NodeList is fine, but converting once avoids any surprises if the DOM mutates
    // while we iterate; clearSearchHighlight is rare enough that the allocation is cheaper than
    // reasoning about it.
    Array.from(marks).forEach(unwrapMark);
};

const shouldSkipTextNode = node => {
    let parent = node.parentElement;
    while (parent !== null) {
        if (parent.tagName === "SCRIPT" || parent.tagName === "STYLE"
            || parent.tagName === "MARK") return true;
        parent = parent.parentElement;
    }
    return false;
};

const applySearchHighlight = (root, query) => {
    clearSearchHighlight(root);
    const trimmed = (query ?? "").trim();
    if (trimmed.length < MinSearchQueryLength) return 0;
    const regex = new RegExp(escapeRegExp(trimmed), "gi");
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
        acceptNode(node) {
            if (node.nodeValue === null || node.nodeValue.length === 0) return NodeFilter.FILTER_REJECT;
            if (shouldSkipTextNode(node)) return NodeFilter.FILTER_REJECT;
            return NodeFilter.FILTER_ACCEPT;
        }
    });
    const textNodes = [];
    let current;
    while ((current = walker.nextNode())) textNodes.push(current);

    let highlightCount = 0;
    textNodes.forEach(node => {
        const text = node.nodeValue;
        regex.lastIndex = 0;
        if (!regex.test(text)) return;
        regex.lastIndex = 0;
        const fragment = document.createDocumentFragment();
        let cursor = 0;
        let match;
        while ((match = regex.exec(text)) !== null) {
            if (match.index > cursor) fragment.appendChild(document.createTextNode(text.slice(cursor, match.index)));
            const mark = document.createElement("mark");
            mark.className = SearchHighlightClass;
            mark.textContent = match[0];
            fragment.appendChild(mark);
            cursor = match.index + match[0].length;
            highlightCount++;
            // A zero-width match (only possible via quantifier tricks) would loop forever.
            if (match[0].length === 0) regex.lastIndex++;
        }
        if (cursor < text.length) fragment.appendChild(document.createTextNode(text.slice(cursor)));
        node.parentNode.replaceChild(fragment, node);
    });
    return highlightCount;
};

// The occurrence the find bar is on. Only one mark carries it; it is re-applied by position after
// a re-walk, because the re-walk replaces every mark element.
const CurrentHighlightClass = "search-highlight-current";

// Marks inside a collapsed block have no boxes: counting them would make "3 / 10" step onto
// something that cannot be shown.
const visibleMarks = root =>
    Array.from(root.querySelectorAll(`mark.${SearchHighlightClass}`)).filter(mark => mark.getClientRects().length > 0);

export function attach(scroller, owner) {
    let pinned = distanceFromBottom(scroller) <= PinThresholdPx;
    let notified = null;
    let watchedElementId = null;
    let watchedElement = null;
    let visibilityNotified = null;

    const notifyVisibility = visible => {
        if (visibilityNotified === visible) return;
        visibilityNotified = visible;
        owner.invokeMethodAsync("OnTurnSummaryVisibilityChanged", visible);
    };

    const visibilityObserver = new IntersectionObserver(entries => {
        const entry = entries.find(item => item.target === watchedElement);
        if (entry) notifyVisibility(entry.isIntersecting && entry.intersectionRatio > 0);
    }, { root: scroller, threshold: 0 });

    // The run snapshot can precede the chat revision that creates the summary element. Remember
    // its id and retry binding on transcript mutations instead of reporting it permanently absent.
    const bindWatchedElement = () => {
        const next = watchedElementId === null
            ? null
            : document.getElementById(watchedElementId);
        const insideScroller = next !== null && scroller.contains(next) ? next : null;
        if (insideScroller === watchedElement && (watchedElement === null || watchedElement.isConnected)) return;
        visibilityObserver.disconnect();
        watchedElement = insideScroller;
        visibilityNotified = null;
        if (watchedElement === null) notifyVisibility(false);
        else visibilityObserver.observe(watchedElement);
    };

    // Blazor only needs to hear about transitions: this fires on every scroll event.
    const notify = () => {
        if (notified === pinned) return;
        notified = pinned;
        owner.invokeMethodAsync("OnPinnedChanged", pinned);
    };

    // The anchor is whatever the user is currently reading — the first message box still crossing
    // the top edge — remembered together with its offset from that edge. If something above it
    // changes height (a markdown block finishing its layout, a tool group being expanded), the
    // anchor is put back where it was instead of being pushed down the screen. Native scroll
    // anchoring does the same thing and usually gets there first; then the correction below
    // computes a no-op, because it compares against where the anchor actually ended up.
    let anchor = null;
    let anchorOffset = 0;
    // Anchoring holds the transcript still against content appearing above — which is exactly
    // what a deliberate move (restoring a remembered reading position) looks like from the
    // outside. Left on, it measures the drift its own caller just introduced and undoes it, so
    // the feed is put back where the move started.
    let anchoring = true;

    // How far down the tree the anchor is allowed to sit. A message is not a small enough unit to
    // anchor to on its own: expanding a tool group inside the very message being read leaves that
    // message's own top exactly where it was, so holding it still holds nothing. Descending to
    // the smallest box that still crosses the top edge is what makes that case work, and a cap
    // keeps the walk bounded on deeply nested markdown.
    const AnchorMaxDepth = 5;

    // Positions are taken from live rectangles rather than offsetTop: at depth the offset parent
    // varies from element to element, while the distance to the scroller's top edge means the
    // same thing everywhere.
    const captureAnchor = () => {
        const edge = scroller.getBoundingClientRect().top;
        const children = scroller.children;
        // Block-level children are laid out top to bottom, so the first one reaching past the top
        // edge is found without measuring the whole transcript.
        let low = 0;
        let high = children.length - 1;
        let found = null;
        while (low <= high) {
            const middle = (low + high) >> 1;
            const child = children[middle];
            if (child.getBoundingClientRect().bottom > edge) {
                found = child;
                high = middle - 1;
            }
            else low = middle + 1;
        }
        for (let depth = 0; found !== null && depth < AnchorMaxDepth; depth++) {
            let next = null;
            for (const child of found.children) {
                if (child.getBoundingClientRect().bottom > edge) {
                    next = child;
                    break;
                }
            }
            if (next === null) break;
            found = next;
        }
        anchor = found;
        anchorOffset = found ? found.getBoundingClientRect().top - edge : 0;
    };

    const restoreAnchor = () => {
        // offsetParent is null for an anchor that has since been hidden — inside a tool row that
        // was collapsed, say. There is nothing to hold it against any more, so leave the position
        // alone rather than acting on a zeroed rectangle.
        if (!anchoring || pinned || anchor === null || !anchor.isConnected || anchor.offsetParent === null) return;
        const edge = scroller.getBoundingClientRect().top;
        const drift = anchor.getBoundingClientRect().top - edge - anchorOffset;
        if (Math.abs(drift) < 1) return;
        scroller.scrollTop += drift;
    };

    const onScroll = () => {
        pinned = distanceFromBottom(scroller) <= PinThresholdPx;
        captureAnchor();
        notify();
    };

    // Content can change without any scroll event, which is exactly the case worth correcting.
    // There are two symmetric rules: a reader above the tail keeps their current anchor, while a
    // reader already at the tail keeps following it. The latter matters for content that grows in
    // place (lazy tool rows, expanded details, rendered diagrams), because it does not travel
    // through MessageFeed.FollowNewContentAsync like a newly published chat message does.
    // Run straight from the observer, NOT via requestAnimationFrame: rAF does not reliably tick
    // when the window is unfocused or the app is in the background, and a correction that only
    // sometimes happens is worse than one that costs a layout read (the same conclusion the
    // history-marker strip reached for its own render loop). MutationObserver already batches
    // per microtask checkpoint, so a streaming answer produces one callback per render, not one
    // per node.
    //
    // Deliberately no "did the height change?" shortcut either. Blazor's diff passes through interim
    // states — a block removed in one batch and re-added in the next — and a run that only acts
    // on a height change can act on the interim one and then skip the batch that would have put
    // it back, leaving the transcript hundreds of pixels off. Re-measuring the anchor every time
    // is self-correcting: while content is only being appended below, the drift is zero and this
    // does nothing.
    const observer = new MutationObserver(() => {
        if (pinned) scroller.scrollTop = scroller.scrollHeight;
        else restoreAnchor();
        bindWatchedElement();
    });
    observer.observe(scroller, { childList: true, subtree: true, characterData: true });

    const toBottom = smooth => {
        pinned = true;
        anchor = null;
        // Instant while content is still arriving: a smooth animation cannot keep up with a feed
        // that grows under it, and reads as lag rather than motion. Smooth is for the pill, where
        // the jump is long and the user needs to see where they went.
        if (smooth) scroller.scrollTo({ top: scroller.scrollHeight, behavior: "smooth" });
        else scroller.scrollTop = scroller.scrollHeight;
        notify();
    };

    // Search: the query last walked and which visible occurrence is current (-1 for none).
    let searchQuery = "";
    let searchIndex = -1;

    const searchPosition = marks => [searchIndex < marks.length ? searchIndex : -1, marks.length];

    const setCurrentMatch = (marks, index, behavior) => {
        for (const mark of scroller.querySelectorAll(`mark.${CurrentHighlightClass}`)) mark.classList.remove(CurrentHighlightClass);
        searchIndex = index >= 0 && index < marks.length ? index : -1;
        if (searchIndex < 0) return;
        const mark = marks[searchIndex];
        mark.classList.add(CurrentHighlightClass);
        if (behavior) mark.scrollIntoView({ behavior, block: "center" });
    };

    // keepPosition is false when the transcript is another chat: the same index there means nothing.
    // Within one chat the current occurrence is remembered as "the n-th mark of this message", not
    // as a global index: a re-walk after a turn's activity loads adds marks above it.
    const highlight = (query, keepPosition) => {
        const trimmed = (query ?? "").trim();
        const keep = keepPosition && trimmed === searchQuery;
        const current = keep ? scroller.querySelector(`mark.${CurrentHighlightClass}`) : null;
        const message = current?.closest('[id^="message-"]') ?? null;
        const ordinal = message ? Array.from(message.querySelectorAll(`mark.${SearchHighlightClass}`)).indexOf(current) : -1;
        if (!keep) searchIndex = -1;
        searchQuery = trimmed;
        applySearchHighlight(scroller, trimmed);
        const marks = visibleMarks(scroller);
        if (message?.isConnected && ordinal >= 0) {
            const again = message.querySelectorAll(`mark.${SearchHighlightClass}`)[ordinal];
            searchIndex = again ? marks.indexOf(again) : -1;
        }
        setCurrentMatch(marks, searchIndex, null);
        return searchPosition(marks);
    };

    // Without a current occurrence, "next" starts from what is on screen rather than from the top
    // of a long transcript the reader has already scrolled through.
    const stepMatch = delta => {
        const marks = visibleMarks(scroller);
        if (marks.length === 0) return searchPosition(marks);
        let next;
        if (searchIndex < 0 || searchIndex >= marks.length) {
            const bounds = scroller.getBoundingClientRect();
            if (delta > 0) {
                next = marks.findIndex(mark => mark.getBoundingClientRect().top >= bounds.top);
                if (next < 0) next = 0;
            } else {
                next = marks.findLastIndex(mark => mark.getBoundingClientRect().bottom <= bounds.bottom);
                if (next < 0) next = marks.length - 1;
            }
        } else {
            next = (searchIndex + delta + marks.length) % marks.length;
        }
        // Instant: F3 held down or pressed quickly must not queue animations behind each other.
        setCurrentMatch(marks, next, "auto");
        return searchPosition(marks);
    };

    // Scrolls a specific message into view — to its first occurrence of the query when it has one.
    // Returns true when the target was in the DOM and the scroll was issued, false when the caller
    // still needs to expand the transcript and try again. "center" leaves some context above and
    // below without it looking like the feed teleported.
    const scrollMessageIntoView = messageId => {
        const element = document.getElementById(`message-${messageId}`);
        if (element === null || !scroller.contains(element)) return false;
        // A chat opened a moment ago may be in the DOM before Blazor has asked for its highlight.
        if (searchQuery.length >= MinSearchQueryLength && element.querySelector(`mark.${SearchHighlightClass}`) === null)
            applySearchHighlight(scroller, searchQuery);
        const marks = visibleMarks(scroller);
        const index = marks.findIndex(mark => element.contains(mark));
        if (index >= 0) setCurrentMatch(marks, index, "smooth");
        else {
            setCurrentMatch(marks, -1, null);
            element.scrollIntoView({ behavior: "smooth", block: "center" });
        }
        return true;
    };

    const onKeyDown = event => {
        if (event.key === "F3" && !event.ctrlKey && !event.altKey && !event.metaKey) {
            // Only while there is something to step through; otherwise F3 stays the browser's.
            if (searchQuery.length < MinSearchQueryLength || scroller.querySelector(`mark.${SearchHighlightClass}`) === null) return;
            event.preventDefault();
            const [index, count] = stepMatch(event.shiftKey ? -1 : 1);
            owner.invokeMethodAsync("OnSearchStepped", index, count);
            return;
        }
        if (event.key !== "End" || event.ctrlKey || event.altKey || event.metaKey || event.shiftKey) return;
        if (isTextEntry(event.target)) return;
        event.preventDefault();
        toBottom(true);
    };

    scroller.addEventListener("scroll", onScroll, { passive: true });
    document.addEventListener("keydown", onKeyDown);
    captureAnchor();

    return {
        // Called after new content rendered. Returns whether the feed actually followed it, so
        // the caller can light up the "there is something new below" affordance when it didn't.
        followIfPinned: () => {
            if (!pinned) return false;
            scroller.scrollTop = scroller.scrollHeight;
            return true;
        },
        scrollToBottom: () => toBottom(false),
        // Called after the feed has been put somewhere deliberately — a restored reading position.
        // The anchor still holds whatever was under the top edge before that move, and the next
        // mutation would drag the transcript back to it; re-reading it makes the new position the
        // one worth holding.
        // Brackets a deliberate move. Off, the anchor stops fighting it; on, the position the move
        // arrived at becomes the one worth holding against everything that lands afterwards.
        setAnchoring: enabled => {
            anchoring = enabled;
            if (!enabled) return;
            anchor = null;
            captureAnchor();
        },
        // A turn can add or remove several transcript blocks at once. Pin its summary row for
        // that mutation so the control the user clicked stays under their pointer.
        anchorElement: id => {
            const element = document.getElementById(id);
            if (element === null || !scroller.contains(element)) return;
            anchor = element;
            anchorOffset = element.getBoundingClientRect().top - scroller.getBoundingClientRect().top;
        },
        // A control at the end of a collapsible region disappears with that region. Anchor the
        // stable content after it instead, so collapsing a long turn reveals the final answer at
        // the same reading position rather than throwing the reader back to the turn heading.
        anchorFollowingElement: id => {
            const element = document.getElementById(id);
            const following = element?.nextElementSibling;
            if (following === null || following === undefined || !scroller.contains(following)) {
                anchor = null;
                return;
            }
            anchor = following;
            anchorOffset = following.getBoundingClientRect().top - scroller.getBoundingClientRect().top;
        },
        focusElement: id => {
            const element = document.getElementById(id);
            if (element === null || !scroller.contains(element)) return;
            element.focus({ preventScroll: true });
        },
        watchElementVisibility: id => {
            watchedElementId = id;
            bindWatchedElement();
        },
        // Both return [current index or -1, visible occurrences].
        applySearchHighlight: (query, keepPosition) => highlight(query, keepPosition === true),
        stepSearchMatch: delta => stepMatch(delta),
        searchPosition: () => searchPosition(visibleMarks(scroller)),
        clearSearchHighlight: () => {
            searchQuery = "";
            searchIndex = -1;
            clearSearchHighlight(scroller);
        },
        scrollMessageIntoView: id => scrollMessageIntoView(id),
        jumpToBottom: () => toBottom(true),
        dispose: () => {
            observer.disconnect();
            visibilityObserver.disconnect();
            scroller.removeEventListener("scroll", onScroll);
            document.removeEventListener("keydown", onKeyDown);
        },
    };
}
