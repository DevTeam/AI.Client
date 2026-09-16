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

export function attach(scroller, owner) {
    let pinned = distanceFromBottom(scroller) <= PinThresholdPx;
    let notified = null;

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
    const observer = new MutationObserver(restoreAnchor);
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

    const onKeyDown = event => {
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
        jumpToBottom: () => toBottom(true),
        dispose: () => {
            observer.disconnect();
            scroller.removeEventListener("scroll", onScroll);
            document.removeEventListener("keydown", onKeyDown);
        },
    };
}
