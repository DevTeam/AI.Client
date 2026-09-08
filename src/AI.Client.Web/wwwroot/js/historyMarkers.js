// Scroll-driven fisheye: discrete, by marker index distance from the scroll position's marker.
// The peak now exceeds 1 (the marker's resting width) too, same reasoning as HoverMaxScale below:
// previously the "active" marker was only ever at its own natural size while everything else
// shrank around it, which barely read as a bulge at all.
const ScrollReach = 4;
const ScrollMinScale = 0.4;
const ScrollMaxScale = 1.25;

// Hover-driven fisheye: continuous, by pixel distance from the cursor. The peak exceeds 1 (the
// marker's resting width) because transform-origin is centred, splitting the extra growth evenly
// left/right. Every marker in the strip now targets a User message (see BuildHistoryEntries in
// MessageFeed.razor), and .workspace-message.user is right-aligned with ample empty space to its
// left, so the growth toward the text side (right) is effectively unconstrained — verified at the
// narrowest supported width (just above the 720px phone-mode cutoff) with hundreds of px to
// spare. The real ceiling is on the OTHER side: growth toward the sidebar (left) is bounded by the
// strip's own 0.5rem inset from its container. 1.55 leaves a comfortable margin there too (the
// container-edge ceiling is closer to ~1.7). See docs/12-ux-decisions.md for the worked-out margin.
const HoverReachPx = 70;
const HoverMinScale = 0.15;
const HoverMaxScale = 1.55;

// The very first and last markers read as "start of history" / "current position" boundaries,
// so they're always at least twice as long as a normal resting marker — a floor, not a
// multiplier, so an endpoint that's also active/hovered still caps out at the same safe maximum
// as any other marker (doubling the peak too would push it past the narrow-gutter clearance
// already tuned into HoverMaxScale).
const ScrollEndpointFloor = ScrollMinScale * 2;
const HoverEndpointFloor = HoverMinScale * 2;

// How far the top/bottom fade reaches into the strip.
const FadeSizePx = 24;

// A few px of slack on the scroll-edge checks: scrollHeight/clientHeight can shift by a
// sub-pixel between calls (font metrics settling, fractional zoom) and a 1px threshold made the
// fade/arrows flicker in and out right at the boundary.
const ScrollEdgeSlackPx = 4;

// How fast the strip scrolls while the pointer rests on an edge arrow — starts slow and ramps up
// to the max over ~0.6s, so the motion reads as a gradual glide rather than a single jump
// (a flat per-tick speed high enough to feel responsive on a long strip covered a short one in
// 1-2 ticks, which looked like an instant snap rather than a scroll).
const HoverScrollStartPx = 2;
const HoverScrollMaxPx = 9;
const HoverScrollAccelTicks = 36;
const HoverScrollIntervalMs = 16;

const GitBranchIconSvg = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="6" cy="5" r="2"/><circle cx="18" cy="7" r="2"/><circle cx="6" cy="19" r="2"/><path d="M6 7v10M8 15h3a7 7 0 0 0 7-6"/></svg>';

// "Which message was on screen" is remembered per (chat, branch) key, not per scroll offset in
// px — heights can shift between sessions (font loading, markdown re-render), but a message id
// is stable, and scrollIntoView puts it back in the same relative spot regardless.
const ScrollPositionKeyPrefix = "ai-client.scroll.v1:";
const ScrollPersistDebounceMs = 400;

// Restores the message a (chat, branch) was last scrolled to, if any was ever recorded — called
// by MessageFeed.razor before deciding whether to fall back to scrolling to the bottom instead.
export function restoreScrollPosition(scroller, key) {
    if (!key) return false;
    const savedId = localStorage.getItem(ScrollPositionKeyPrefix + key);
    if (!savedId) return false;
    const target = document.getElementById(savedId);
    if (!target) return false;
    target.scrollIntoView({ block: "start" });
    return true;
}

// "14:32" for today, "14:32 05.03" for any other day — the date is dropped when it wouldn't add
// information, since "today" is the overwhelmingly common case while browsing recent history.
const formatMessageTime = isoString => {
    if (!isoString) return "";
    const created = new Date(isoString);
    if (Number.isNaN(created.getTime())) return "";
    const now = new Date();
    const time = `${String(created.getHours()).padStart(2, "0")}:${String(created.getMinutes()).padStart(2, "0")}`;
    const isToday = created.getFullYear() === now.getFullYear() && created.getMonth() === now.getMonth() && created.getDate() === now.getDate();
    if (isToday) return time;
    const shortDate = `${String(created.getDate()).padStart(2, "0")}.${String(created.getMonth() + 1).padStart(2, "0")}`;
    return `${time} ${shortDate}`;
};

export function attach(strip, scroller, scrollKey) {
    let offsets = [];
    let measuredHeight = -1;
    let hoverY = null;
    let scrollPersistTimer = null;
    // Lives as a sibling of the strip, not inside it: the strip clips its own overflow-x to
    // support the internal auto-scroll, which would hide a tooltip anchored inside it.
    const tip = strip.parentElement.querySelector(":scope > .history-marker-tip");
    const hintUp = strip.parentElement.querySelector(":scope > .history-scroll-hint-up");
    const hintDown = strip.parentElement.querySelector(":scope > .history-scroll-hint-down");

    const markers = () => [...strip.querySelectorAll(".history-marker")];
    const isEndpoint = (index, count) => index === 0 || index === count - 1;

    // offsetTop is cheap and stable as long as the feed layout did not change, so the
    // scroll handler never has to measure every message again.
    const measure = () => {
        measuredHeight = scroller.scrollHeight;
        offsets = markers().map(marker => document.getElementById(marker.dataset.target)?.offsetTop ?? 0);
    };

    const scrollActiveIndex = list => {
        // A quarter down the viewport reads as "what I am looking at", but at the very edges
        // that anchor lies past the first / before the last message, so the edges are pinned.
        if (scroller.scrollTop <= 2) return 0;
        if (scroller.scrollTop + scroller.clientHeight >= scroller.scrollHeight - 2) return list.length - 1;
        const anchor = scroller.scrollTop + scroller.clientHeight * 0.25;
        let active = 0;
        for (let index = 0; index < offsets.length; index++) {
            if (offsets[index] <= anchor) active = index;
        }
        return active;
    };

    // Tracks which marker's content is currently painted into the tip DOM, so showTip() only
    // rebuilds it (innerHTML included) when the nearest marker actually changes — not on every
    // mousemove tick. showTip used to run unconditionally on every tick via render(); that was
    // already wasteful with textContent, and would be a real cost with innerHTML (the browser
    // re-parses the rendered-markdown HTML on every call).
    let lastTipMarker = null;
    const hideTip = () => { tip.classList.remove("visible"); lastTipMarker = null; };

    // An optional meta line (time, short date, branch badge), then the question + a blank-line
    // gap + the final answer, each clamped to 5 lines by CSS (-webkit-line-clamp). The
    // question/answer are already-sanitized markdown HTML (see IMarkdownRenderer / MessageFeed's
    // RenderMarkdownCached, reused as-is here — same HTML the message body itself renders) set via
    // innerHTML; the meta line stays textContent since it's plain text built here, not markdown.
    const showTip = marker => {
        if (marker === lastTipMarker) { tip.classList.add("visible"); return; }
        const question = marker.dataset.questionHtml;
        if (!question) { hideTip(); return; }
        lastTipMarker = marker;

        tip.replaceChildren();

        // Time, short date (only when not today), then the branch badge — all on one line, in
        // that order.
        const time = formatMessageTime(marker.dataset.created);
        const hasFork = marker.dataset.fork === "true";
        if (time || hasFork) {
            const meta = document.createElement("div");
            meta.className = "history-marker-tip-meta";
            if (time) {
                const timeEl = document.createElement("span");
                timeEl.className = "history-marker-tip-time";
                timeEl.textContent = time;
                meta.appendChild(timeEl);
            }
            if (hasFork) {
                const badge = document.createElement("span");
                badge.className = "history-marker-tip-fork";
                badge.innerHTML = `${GitBranchIconSvg}<span>Has branches</span>`;
                meta.appendChild(badge);
            }
            tip.appendChild(meta);
        }

        const questionEl = document.createElement("div");
        questionEl.className = "history-marker-tip-question";
        questionEl.innerHTML = question;
        tip.appendChild(questionEl);

        const answer = marker.dataset.answerHtml;
        if (answer) {
            const answerEl = document.createElement("div");
            answerEl.className = "history-marker-tip-answer";
            answerEl.innerHTML = answer;
            tip.appendChild(answerEl);
        }

        // Positioned from live rects, not offsetTop: the tip's offset parent is no longer the
        // strip (see the comment where `tip` is looked up), so offsetTop would be relative to
        // the wrong box, and the strip itself scrolls internally on top of that.
        const parentRect = tip.offsetParent.getBoundingClientRect();
        const markerRect = marker.getBoundingClientRect();
        const stripRect = strip.getBoundingClientRect();
        tip.style.left = `${stripRect.right - parentRect.left + 8}px`;

        // The tip is vertically centred on the marker (CSS translateY(-50%)), so a marker near
        // either end of the strip would otherwise centre a multi-line tip partly above or below
        // the message column. tipHeight varies with the question/answer being shown, so this is
        // measured live rather than assumed — clamp the centre so the whole box stays on-screen.
        const tipHeight = tip.offsetHeight;
        const rawCenter = markerRect.top + markerRect.height / 2 - parentRect.top;
        const minCenter = tipHeight / 2 + 4;
        const maxCenter = parentRect.height - tipHeight / 2 - 4;
        tip.style.top = `${Math.min(Math.max(rawCenter, minCenter), maxCenter)}px`;
        tip.classList.add("visible");
    };

    // A soft alpha fade at whichever edge still has content to scroll to — full opacity once
    // there is nothing left in that direction. Written as an inline mask (not a class) because
    // it depends on live scroll position, recomputed on every render() alongside everything else.
    // The chevron hints share the same canScrollUp/canScrollDown booleans: same signal, two forms
    // (a soft fade plus an explicit arrow), so they always agree with each other. This reads the
    // STRIP's own scroll (not the message feed's) — the strip has its own independent scrollbar
    // (overflow: hidden auto) and can be at a different position/extent than the feed.
    const updateFade = () => {
        const canScrollUp = strip.scrollTop > ScrollEdgeSlackPx;
        const canScrollDown = strip.scrollTop + strip.clientHeight < strip.scrollHeight - ScrollEdgeSlackPx;
        const top = canScrollUp ? `transparent 0, black ${FadeSizePx}px` : "black 0";
        const bottom = canScrollDown ? `black calc(100% - ${FadeSizePx}px), transparent 100%` : "black 100%";
        const mask = `linear-gradient(to bottom, ${top}, ${bottom})`;
        strip.style.maskImage = mask;
        strip.style.webkitMaskImage = mask;
        hintUp.classList.toggle("visible", canScrollUp);
        hintDown.classList.toggle("visible", canScrollDown);
    };

    // Debounced (400ms after scrolling settles), not written on every scroll event — this is a
    // localStorage write, and unlike everything else in this module it deliberately does NOT run
    // synchronously on every scroll tick. Scans every message (not just the user-question
    // markers, which no longer cover assistant replies) for whichever one sits at the same
    // "quarter down the viewport" anchor the fisheye itself uses, so re-opening the chat lands on
    // the same message the marker-based logic would have picked as "active".
    const persistScrollPosition = () => {
        if (!scrollKey) return;
        clearTimeout(scrollPersistTimer);
        scrollPersistTimer = setTimeout(() => {
            const articles = [...scroller.querySelectorAll(".workspace-message[id]")];
            if (articles.length === 0) return;
            const anchor = scroller.scrollTop + scroller.clientHeight * 0.25;
            let current = articles[0];
            for (const article of articles) {
                if (article.offsetTop <= anchor) current = article; else break;
            }
            localStorage.setItem(ScrollPositionKeyPrefix + scrollKey, current.id);
        }, ScrollPersistDebounceMs);
    };

    // Called directly from every listener below, not through requestAnimationFrame: rAF is
    // throttled or skipped by the browser in several ordinary situations (unfocused window,
    // reduced/battery-saver states, backgrounded preview panes), and a hover effect that only
    // works sometimes is worse than one that costs a little more CPU on a handful of buttons.
    const render = () => {
        const list = markers();
        if (list.length === 0) return;
        if (offsets.length !== list.length || scroller.scrollHeight !== measuredHeight) measure();

        // Nothing to navigate while everything already fits on screen.
        const idle = scroller.scrollHeight <= scroller.clientHeight + 4;
        strip.classList.toggle("idle", idle);
        if (idle) {
            hintUp.classList.remove("visible");
            hintDown.classList.remove("visible");
        } else {
            updateFade();
        }

        if (hoverY === null) {
            const active = scrollActiveIndex(list);
            for (let index = 0; index < list.length; index++) {
                const nearness = Math.max(0, 1 - Math.abs(index - active) / ScrollReach);
                let scale = ScrollMinScale + (ScrollMaxScale - ScrollMinScale) * nearness;
                if (isEndpoint(index, list.length)) scale = Math.max(scale, ScrollEndpointFloor);
                list[index].style.transform = `scaleX(${scale.toFixed(3)})`;
                list[index].classList.toggle("active", index === active);
            }
            hideTip();

            // Skipped while an edge arrow is being hovered: that gesture is the user manually
            // scrolling the strip, and this "snap the active marker back into view" behaviour
            // would otherwise immediately fight it on every tick.
            if (!arrowHovering) {
                const current = list[active];
                const stripBox = strip.getBoundingClientRect();
                const currentBox = current.getBoundingClientRect();
                if (currentBox.top < stripBox.top || currentBox.bottom > stripBox.bottom) {
                    strip.scrollTop += currentBox.top - stripBox.top - strip.clientHeight / 2;
                }
            }
        } else {
            let nearestIndex = 0;
            let nearestDistance = Infinity;
            const centers = list.map(marker => marker.offsetTop + marker.offsetHeight / 2);
            for (let index = 0; index < list.length; index++) {
                const distance = Math.abs(centers[index] - hoverY);
                if (distance < nearestDistance) { nearestDistance = distance; nearestIndex = index; }
            }
            for (let index = 0; index < list.length; index++) {
                const falloff = Math.max(0, 1 - Math.abs(centers[index] - hoverY) / HoverReachPx);
                const eased = falloff * falloff;
                let scale = HoverMinScale + (HoverMaxScale - HoverMinScale) * eased;
                if (isEndpoint(index, list.length)) scale = Math.max(scale, HoverEndpointFloor);
                list[index].style.transform = `scaleX(${scale.toFixed(3)})`;
                list[index].classList.toggle("active", index === nearestIndex);
            }
            showTip(list[nearestIndex]);
        }
    };

    const onClick = event => {
        const marker = event.target.closest(".history-marker");
        if (!marker) return;
        document.getElementById(marker.dataset.target)?.scrollIntoView({ behavior: "smooth", block: "start" });
    };

    const onMouseMove = event => {
        hoverY = event.clientY - strip.getBoundingClientRect().top + strip.scrollTop;
        render();
    };

    const onMouseLeave = () => {
        hoverY = null;
        render();
    };

    // Hovering an edge arrow scrolls the STRIP itself continuously in that direction (not the
    // message feed — the strip has its own independent scroll) — setInterval, not
    // requestAnimationFrame: rAF is throttled or skipped in several ordinary situations (see the
    // render() comment above), and a scroll hint that only sometimes scrolls is worse than the
    // fixed tick rate here. The native scrollTop setter clamps at the real edge on its own, and
    // render() (called every tick, since the strip has no native "scroll" listener of its own)
    // hides the arrow once there's nothing left that way.
    let arrowHovering = false;
    let hoverScrollTimer = null;
    const stopHoverScroll = () => {
        arrowHovering = false;
        hintUp.classList.remove("scrolling");
        hintDown.classList.remove("scrolling");
        if (hoverScrollTimer === null) return;
        clearInterval(hoverScrollTimer);
        hoverScrollTimer = null;
    };
    // The `.scrolling` class (an accent-colour highlight, see app.css) is the visible confirmation
    // that the hold-to-scroll gesture actually registered — the ramp-up alone can still read as
    // "nothing happened yet" for the first tick or two.
    const startHoverScroll = (direction, hint) => {
        stopHoverScroll();
        arrowHovering = true;
        hint.classList.add("scrolling");
        let tick = 0;
        hoverScrollTimer = setInterval(() => {
            tick++;
            const progress = Math.min(1, tick / HoverScrollAccelTicks);
            const speed = HoverScrollStartPx + (HoverScrollMaxPx - HoverScrollStartPx) * progress;
            strip.scrollTop += direction * speed;
            render();
        }, HoverScrollIntervalMs);
    };
    const onHintUpEnter = () => startHoverScroll(-1, hintUp);
    const onHintDownEnter = () => startHoverScroll(1, hintDown);

    const onScroll = () => { render(); persistScrollPosition(); };

    scroller.addEventListener("scroll", onScroll, { passive: true });
    strip.addEventListener("click", onClick);
    strip.addEventListener("mousemove", onMouseMove);
    strip.addEventListener("mouseleave", onMouseLeave);
    hintUp.addEventListener("mouseenter", onHintUpEnter);
    hintUp.addEventListener("mouseleave", stopHoverScroll);
    hintDown.addEventListener("mouseenter", onHintDownEnter);
    hintDown.addEventListener("mouseleave", stopHoverScroll);
    render();

    return {
        refresh: () => { measuredHeight = -1; render(); },
        dispose: () => {
            stopHoverScroll();
            clearTimeout(scrollPersistTimer);
            scroller.removeEventListener("scroll", onScroll);
            strip.removeEventListener("click", onClick);
            strip.removeEventListener("mousemove", onMouseMove);
            strip.removeEventListener("mouseleave", onMouseLeave);
            hintUp.removeEventListener("mouseenter", onHintUpEnter);
            hintUp.removeEventListener("mouseleave", stopHoverScroll);
            hintDown.removeEventListener("mouseenter", onHintDownEnter);
            hintDown.removeEventListener("mouseleave", stopHoverScroll);
        }
    };
}
