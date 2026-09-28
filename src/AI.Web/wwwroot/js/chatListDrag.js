// Reordering of the sidebar chat list. Pointer events rather than HTML5 drag and drop: the native
// API has no touch support, draws its own ghost and gives no say over where the drop line goes.
//
// Only pinned chats have a manual order. Every root chat row takes part so that dragging across
// the pinned/unpinned boundary pins or unpins, but the drop line is drawn only where the drop
// actually decides a position: inside the pinned zone. Branch rows follow their chat and are never
// a drop target of their own.

const listSelector = ".workspace-chat-list";
const rowSelector = ".chat-nav-row[data-chat-id]";
const dividerSelector = ".chat-pin-divider";
// A press that moves less than this is a click on the chat, not a drag.
const mouseSlop = 5;
// A touch has to rest this long before it becomes a drag; moving earlier is a scroll.
const longPressDelay = 400;
const touchSlop = 8;
const autoScrollEdge = 32;
const autoScrollStep = 12;
// Holding a dragged chat over "Show N more" opens the next page, so a chat can reach a spot
// that was paged away.
const expandDelay = 600;
const moveDuration = 180;

// The sidebar is attached once; the chat list inside it is re-created whenever the selected
// project changes, so it is looked up on every press instead of being held.
export function attach(root, dotNet) {
    let press = null;
    let drag = null;
    let positions = null;

    const rows = () => [...root.querySelectorAll(`${listSelector} ${rowSelector}`)];

    function onPointerDown(event) {
        if (event.button !== 0 || drag) return;
        const row = event.target.closest(rowSelector);
        const list = row?.closest(listSelector);
        if (!list || !root.contains(list)) return;
        if (event.target.closest(".chat-row-action, .chat-menu, input, textarea")) return;
        press = {
            row, list, pointerId: event.pointerId, x: event.clientX, y: event.clientY,
            touch: event.pointerType === "touch", timer: 0
        };
        if (press.touch) {
            press.timer = setTimeout(() => begin(press.x, press.y), longPressDelay);
            // Only a non-passive listener may refuse the touch scroll, so it exists only while a
            // chat is pressed rather than slowing every scroll on the page.
            document.addEventListener("touchmove", onTouchMove, { passive: false });
        }
        window.addEventListener("pointermove", onPointerMove, true);
        window.addEventListener("pointerup", onPointerUp, true);
        window.addEventListener("pointercancel", onPointerCancel, true);
    }

    function onPointerMove(event) {
        if (!press || event.pointerId !== press.pointerId) return;
        if (!drag) {
            const distance = Math.hypot(event.clientX - press.x, event.clientY - press.y);
            if (press.touch) {
                // Moving before the long press elapsed is the start of a scroll: give it back.
                if (distance > touchSlop) release();
                return;
            }
            if (distance < mouseSlop) return;
            begin(event.clientX, event.clientY);
        }
        event.preventDefault();
        update(event.clientX, event.clientY);
    }

    function onPointerUp(event) {
        if (!press || event.pointerId !== press.pointerId) return;
        if (drag) finish(true);
        else release();
    }

    function onPointerCancel(event) {
        if (!press || event.pointerId !== press.pointerId) return;
        if (drag) finish(false);
        else release();
    }

    function onKeyDown(event) {
        if (drag && event.key === "Escape") {
            event.preventDefault();
            event.stopPropagation();
            finish(false);
        }
    }

    function onTouchMove(event) {
        if (drag) event.preventDefault();
    }

    // On a touch screen a long press also asks for the context menu, which would open the chat
    // menu on top of the drag that the same press starts.
    function onContextMenu(event) {
        if (drag || press?.touch) {
            event.preventDefault();
            event.stopPropagation();
        }
    }

    function begin(x, y) {
        if (!press || drag) return;
        clearTimeout(press.timer);
        const { row, list } = press;
        const bounds = row.getBoundingClientRect();
        const ghost = document.createElement("div");
        ghost.className = "chat-drag-ghost";
        ghost.setAttribute("aria-hidden", "true");
        ghost.style.width = `${bounds.width}px`;
        ghost.append(row.querySelector(".chat-link")?.cloneNode(true) ?? row.cloneNode(true));
        const hint = document.createElement("span");
        hint.className = "chat-drag-hint";
        ghost.append(hint);
        document.body.append(ghost);
        const line = document.createElement("div");
        line.className = "chat-drop-line";
        line.setAttribute("aria-hidden", "true");
        line.hidden = true;
        list.append(line);
        drag = {
            id: row.dataset.chatId,
            pinned: row.dataset.pinned === "true",
            row, list, ghost, hint, line,
            offsetX: x - bounds.left,
            offsetY: y - bounds.top,
            target: null,
            scroller: list.closest(".workspace-sidebar") ?? list.parentElement,
            scrollTimer: 0,
            expandTimer: 0,
            expandButton: null,
            y
        };
        row.classList.add("dragging");
        list.classList.add("chat-list-dragging");
        if (press.touch) navigator.vibrate?.(10);
        dotNet.invokeMethodAsync("OnChatDragStateChanged", true);
        update(x, y);
    }

    function update(x, y) {
        drag.y = y;
        drag.ghost.style.transform = `translate(${x - drag.offsetX}px, ${y - drag.offsetY}px)`;
        drag.target = findTarget(drag.list, y);
        drawTarget();
        autoScroll(y);
        watchExpand(x, y);
    }

    // The drop goes in front of the first row, or the divider, whose middle is below the pointer.
    // Rows and the divider are in the DOM in the order they are on screen.
    function findTarget(list, y) {
        const items = [...list.querySelectorAll(`${rowSelector}, ${dividerSelector}`)];
        let dividerIndex = items.findIndex(item => item.matches(dividerSelector));
        // More pinned chats than one page shows: the divider is paged away with the rest of the
        // zone, and every visible row is pinned.
        if (dividerIndex < 0 && items.some(item => item.dataset.pinned === "true")) dividerIndex = items.length;
        let index = items.findIndex(item => {
            const bounds = item.getBoundingClientRect();
            return y < bounds.top + bounds.height / 2;
        });
        if (index < 0) index = items.length;
        // The divider is rendered whenever a drop could pin; without it nothing is pinned here.
        if (dividerIndex < 0 || index > dividerIndex) return { pinned: false, beforeId: null, lineY: 0 };
        const before = items.slice(index, dividerIndex).find(item => item.matches(rowSelector));
        // The line sits on top of the item the drop goes in front of, or under the last item.
        const anchor = items[index] ?? null;
        const lineY = anchor ? anchor.getBoundingClientRect().top : items.at(-1).getBoundingClientRect().bottom;
        return { pinned: true, beforeId: before?.dataset.chatId ?? null, lineY };
    }

    // Unpinned chats follow activity, so moving one among them changes nothing; a pinned chat
    // dropped right in front of itself or of its successor stays where it is.
    function isNoOp(state, target) {
        if (target.pinned !== state.pinned) return false;
        if (!target.pinned) return true;
        const pinnedRows = [...state.list.querySelectorAll(rowSelector)].filter(row => row.dataset.pinned === "true");
        const index = pinnedRows.findIndex(row => row.dataset.chatId === state.id);
        const next = pinnedRows[index + 1]?.dataset.chatId ?? null;
        return target.beforeId === state.id || target.beforeId === next;
    }

    function drawTarget() {
        const target = drag.target;
        const noOp = isNoOp(drag, target);
        drag.hint.textContent = noOp ? "" : !target.pinned ? "Unpin" : drag.pinned ? "" : "Pin";
        drag.list.classList.toggle("chat-drop-unpin", !noOp && !target.pinned);
        if (noOp || !target.pinned) {
            drag.line.hidden = true;
            return;
        }
        const listTop = drag.list.getBoundingClientRect().top;
        drag.line.hidden = false;
        drag.line.style.top = `${target.lineY - listTop}px`;
    }

    function autoScroll(y) {
        clearInterval(drag.scrollTimer);
        const bounds = drag.scroller.getBoundingClientRect();
        const direction = y < bounds.top + autoScrollEdge ? -1 : y > bounds.bottom - autoScrollEdge ? 1 : 0;
        if (!direction) return;
        // An interval, not animation frames: frames are not delivered in every host this runs in.
        drag.scrollTimer = setInterval(() => {
            if (!drag) return;
            drag.scroller.scrollTop += direction * autoScrollStep;
            drag.target = findTarget(drag.list, drag.y);
            drawTarget();
        }, 16);
    }

    function watchExpand(x, y) {
        const button = document.elementsFromPoint(x, y).find(element => element.matches(".chat-list-more")) ?? null;
        if (button === drag.expandButton) return;
        clearTimeout(drag.expandTimer);
        drag.expandButton = button;
        if (button) drag.expandTimer = setTimeout(() => button.click(), expandDelay);
    }

    function finish(commit) {
        const state = drag;
        const target = state.target;
        release();
        if (!commit || !target || isNoOp(state, target)) return;
        // The click that ends a mouse drag lands on the chat under the pointer; it is not a selection.
        window.addEventListener("click", swallowClick, { capture: true, once: true });
        setTimeout(() => window.removeEventListener("click", swallowClick, { capture: true }), 0);
        capturePositions();
        dotNet.invokeMethodAsync("OnChatDropped", state.id, target.pinned, target.beforeId);
    }

    function release() {
        if (press) clearTimeout(press.timer);
        press = null;
        window.removeEventListener("pointermove", onPointerMove, true);
        window.removeEventListener("pointerup", onPointerUp, true);
        window.removeEventListener("pointercancel", onPointerCancel, true);
        document.removeEventListener("touchmove", onTouchMove, { passive: false });
        if (!drag) return;
        clearInterval(drag.scrollTimer);
        clearTimeout(drag.expandTimer);
        drag.ghost.remove();
        drag.line.remove();
        drag.row.classList.remove("dragging");
        drag.list.classList.remove("chat-list-dragging", "chat-drop-unpin");
        drag = null;
        dotNet.invokeMethodAsync("OnChatDragStateChanged", false);
    }

    function swallowClick(event) {
        event.preventDefault();
        event.stopPropagation();
    }

    // FLIP: remember where each row was and, once the list has re-rendered, slide the rows from
    // there to their new place so a chat that moved can be followed with the eye.
    function capturePositions() {
        positions = new Map(rows().map(row => [row.dataset.chatId, row.getBoundingClientRect().top]));
    }

    function playPositions() {
        const previous = positions;
        positions = null;
        if (!previous || window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
        for (const row of rows()) {
            const before = previous.get(row.dataset.chatId);
            if (before === undefined) continue;
            const delta = before - row.getBoundingClientRect().top;
            if (Math.abs(delta) < 1) continue;
            row.style.transition = "none";
            row.style.transform = `translateY(${delta}px)`;
            // Reading layout commits the offset before the transition back to the new place starts.
            row.getBoundingClientRect();
            row.style.transition = `transform ${moveDuration}ms ease-out`;
            row.style.transform = "";
            setTimeout(() => { row.style.transition = ""; }, moveDuration);
        }
    }

    // Blazor re-inserts a moved row, and a node taken out of the DOM loses focus with it.
    function focusChat(id) {
        root.querySelector(`${listSelector} ${rowSelector}[data-chat-id="${CSS.escape(id)}"] .chat-link`)?.focus();
    }

    root.addEventListener("pointerdown", onPointerDown);
    window.addEventListener("keydown", onKeyDown, true);
    window.addEventListener("contextmenu", onContextMenu, true);

    return {
        capturePositions,
        playPositions,
        focusChat,
        dispose() {
            release();
            root.removeEventListener("pointerdown", onPointerDown);
            window.removeEventListener("keydown", onKeyDown, true);
            window.removeEventListener("contextmenu", onContextMenu, true);
        }
    };
}
