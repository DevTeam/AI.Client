// The hover bend. It is centred on the marker nearest the cursor, not on the cursor itself, and
// falls off by marker count, so it is always symmetric around the hovered dash: following the raw
// cursor made it lopsided whenever the pointer sat between two dashes. The peak is four resting
// dash lengths and the falloff eases out over about four markers on either side. At rest the strip
// is flat; what is on screen is shown by colour (see .in-view in app.css).
const BendReachMarkers = 5;
const BendMaxScale = 4;
const BendCurve = 1.5;

// Writes one marker's bend: the dash scale, plus a 0..1 closeness as --history-marker-focus,
// from which app.css brightens the dashes along the bend.
const applyBend = (marker, steps) => {
    const focus = steps === null ? 0 : Math.max(0, 1 - steps / BendReachMarkers) ** BendCurve;
    marker.style.setProperty("--history-marker-scale", (1 + (BendMaxScale - 1) * focus).toFixed(3));
    marker.style.setProperty("--history-marker-focus", focus.toFixed(3));
};

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

// Exact feed position per (chat, branch). Bottom is stored semantically so a transcript that
// grows between visits still opens at its end; every other position uses the real scrollTop.
const ScrollPositionKeyPrefix = "ai-client.scroll.v2:";
const ScrollPersistDebounceMs = 400;
const scrollPositions = new Map();
const restoringScrollKeys = new Set();

const captureScrollPosition = scroller =>
    scroller.scrollTop + scroller.clientHeight >= scroller.scrollHeight - ScrollEdgeSlackPx
        ? { bottom: true }
        : { scrollTop: scroller.scrollTop };

const persistCapturedScrollPosition = (key, saved) => {
    scrollPositions.set(key, saved);
    localStorage.setItem(ScrollPositionKeyPrefix + key, JSON.stringify(saved));
};

// A frame, or a timer if frames are not coming. requestAnimationFrame stops ticking in a tab that
// is not being painted — another window in front, another tab selected, a headless viewport — and
// the restore loop below would then wait forever, leaving the feed wherever the switch left it and
// the caller's await unresolved. The timer keeps the loop advancing at a coarser rate instead,
// which is all it needs: it only re-applies a scroll target until the height stops moving.
const FrameFallbackMs = 100;

const nextFrame = () => new Promise(resolve => {
    const frame = requestAnimationFrame(() => {
        clearTimeout(fallback);
        resolve();
    });
    const fallback = setTimeout(() => {
        cancelAnimationFrame(frame);
        resolve();
    }, FrameFallbackMs);
});

const restoreAfterLayoutSettles = async (scroller, key, saved) => {
    const startedAt = performance.now();
    let lastHeight = -1;
    let lastHeightChangeAt = startedAt;
    restoringScrollKeys.add(key);
    try {
        // Blazor has completed its render when this function is called, but the browser can still
        // be laying out the large markdown transcript. Re-apply the target until its scrollHeight
        // has been stable long enough; otherwise scrollTop is clamped to the shorter interim feed.
        while (performance.now() - startedAt < 1500) {
            await nextFrame();
            const height = scroller.scrollHeight;
            if (height !== lastHeight) {
                lastHeight = height;
                lastHeightChangeAt = performance.now();
            }
            scroller.scrollTop = saved.bottom === true ? height : saved.scrollTop;
            const elapsed = performance.now() - startedAt;
            if (elapsed >= 300 && performance.now() - lastHeightChangeAt >= 100) break;
        }
        scrollPositions.set(key, saved);
    } finally {
        restoringScrollKeys.delete(key);
    }
};

export async function restoreScrollPosition(scroller, key) {
    if (!key) return false;
    let saved = scrollPositions.get(key);
    if (!saved) {
        const savedValue = localStorage.getItem(ScrollPositionKeyPrefix + key);
        if (!savedValue) return false;
        try {
            saved = JSON.parse(savedValue);
        } catch {
            return false;
        }
        scrollPositions.set(key, saved);
    }
    if (saved?.bottom === true) {
        await restoreAfterLayoutSettles(scroller, key, saved);
        return true;
    }
    if (!Number.isFinite(saved?.scrollTop)) return false;
    await restoreAfterLayoutSettles(scroller, key, saved);
    return true;
}

export function saveScrollPosition(scroller, key) {
    if (!key) return;
    persistCapturedScrollPosition(key, captureScrollPosition(scroller));
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
    // Sentinel: the nearest marker the cursor was on the LAST time a ratchet tick could have
    // fired. Cleared on mouseleave so the next entry into the strip plays its first tick even
    // when it lands on the same marker the cursor was on before — without this reset, lifting
    // the cursor off the strip and putting it back on the same mark (or one close by) would
    // play nothing, and the strip would feel "broken" until the cursor actually moved.
    let lastRatchetIndex = null;
    let scrollPersistTimer = null;
    // Lives as a sibling of the strip, not inside it: the strip clips its own overflow-x to
    // support the internal auto-scroll, which would hide a tooltip anchored inside it.
    const tip = strip.parentElement.querySelector(":scope > .history-marker-tip");
    const hintUp = strip.parentElement.querySelector(":scope > .history-scroll-hint-up");
    const hintDown = strip.parentElement.querySelector(":scope > .history-scroll-hint-down");

    const markers = () => [...strip.querySelectorAll(".history-marker")];

    // offsetTop is cheap and stable as long as the feed layout did not change, so the
    // scroll handler never has to measure every message again.
    const measure = () => {
        measuredHeight = scroller.scrollHeight;
        offsets = markers().map(marker => document.getElementById(marker.dataset.target)?.offsetTop ?? 0);
    };

    // A turn runs from its question to the next question (or the end of the feed), so it covers
    // the steps and the final answer in between; it is in view when any of it is on screen.
    const isTurnInView = index => {
        const top = offsets[index];
        const bottom = index + 1 < offsets.length ? offsets[index + 1] : scroller.scrollHeight;
        return bottom > scroller.scrollTop && top < scroller.scrollTop + scroller.clientHeight;
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
    // Read the already-sanitized HTML from the visible message DOM only when a tip is shown.
    // Storing the same potentially-large HTML in every marker's data attributes doubled the
    // transcript payload Blazor had to build and the browser had to retain on every chat switch.
    // The meta line stays textContent since it's plain text built here, not markdown.
    const messageHtml = targetId => targetId
        ? document.getElementById(targetId)?.querySelector(".markdown-content")?.innerHTML ?? ""
        : "";
    const showTip = marker => {
        if (marker === lastTipMarker) { tip.classList.add("visible"); return; }
        const question = marker.dataset.questionText || messageHtml(marker.dataset.questionTarget);
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

        const answer = messageHtml(marker.dataset.answerTarget);
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

    // Keep the latest position for page reloads without writing localStorage on every scroll
    // tick. Chat/branch navigation separately saves synchronously before replacing the DOM.
    const persistScrollPosition = () => {
        if (!scrollKey || restoringScrollKeys.has(scrollKey)) return;
        const saved = captureScrollPosition(scroller);
        scrollPositions.set(scrollKey, saved);
        clearTimeout(scrollPersistTimer);
        scrollPersistTimer = setTimeout(() => {
            // A synchronous navigation save may have superseded this event while the timeout
            // was pending. Only flush the exact snapshot that is still current for this key.
            if (scrollPositions.get(scrollKey) === saved) {
                localStorage.setItem(ScrollPositionKeyPrefix + scrollKey, JSON.stringify(saved));
            }
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
                applyBend(list[index], null);
                list[index].classList.toggle("in-view", isTurnInView(index));
                list[index].classList.remove("active");
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
            // Ratchet: a tick on each CROSS into a new marker, not on every pixel of movement.
            // lastRatchetIndex starts as null so the cursor's first entry into the strip also
            // ticks (see the reset in onMouseLeave below — without that, leaving and re-entering
            // the same strip at the same mark would play nothing). window.aiClientPlayRatchetTick
            // is defined by js/hoverSound.js (loaded as a plain script in index.html); the feature
            // gracefully no-ops when the page has not loaded the script yet, or when the browser
            // has blocked audio until a user gesture.
            if (lastRatchetIndex !== nearestIndex && typeof window.aiClientPlayRatchetTick === "function") {
                window.aiClientPlayRatchetTick();
                lastRatchetIndex = nearestIndex;
            }
            for (let index = 0; index < list.length; index++) {
                applyBend(list[index], Math.abs(index - nearestIndex));
                list[index].classList.toggle("in-view", isTurnInView(index));
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
        // Reset so the next entry into the strip plays its first tick, even when the cursor
        // comes back to the same marker it left from. With this null the render() in the
        // hover branch above ticks on the very first mousemove of any new hover (because
        // lastRatchetIndex is null !== nearestIndex); without it, a hover that never crosses
        // a marker boundary would be silent from entry to exit.
        lastRatchetIndex = null;
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
            // Navigation already saved synchronously. Never let an older delayed write replace it.
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
