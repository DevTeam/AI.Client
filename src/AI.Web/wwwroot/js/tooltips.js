// One tooltip for the whole app, in the chrome of the message comment editor. Every element with a
// title attribute gets it instead of the browser's own tooltip, which cannot be styled and looks
// different in every web view; comments on message text use it through window.appTooltip.
//
// A title is moved to data-tooltip on the first hover, which is what keeps the native tooltip
// away. Blazor diffs against what it rendered, not against the page, so it leaves the moved
// attribute alone until the title itself changes — and then sets it again, and the next hover
// moves the new text. An element that would lose its only accessible name keeps it as aria-label.
(() => {
    const SHOW_DELAY_MS = 450;
    let element = null;
    let owner = null;
    let timer = 0;

    function tooltipElement() {
        if (!element) {
            element = document.createElement("div");
            element.className = "app-tooltip";
            element.setAttribute("role", "tooltip");
            element.hidden = true;
            document.body.appendChild(element);
        }
        return element;
    }

    // Places the tooltip above the anchor, or below it when there is no room, centred on x.
    function place(anchor, x) {
        const tooltip = tooltipElement();
        const width = tooltip.offsetWidth;
        const height = tooltip.offsetHeight;
        const left = Math.max(8, Math.min(x - width / 2, window.innerWidth - width - 8));
        const above = anchor.top - height - 4;
        tooltip.style.left = `${left}px`;
        tooltip.style.top = `${above >= 8 ? above : Math.min(anchor.bottom + 4, window.innerHeight - height - 8)}px`;
    }

    // Shows one paragraph per text, so several comments on one fragment stay apart.
    function show(texts, anchor, x, key) {
        const tooltip = tooltipElement();
        if (tooltip.dataset.key !== key) {
            tooltip.dataset.key = key;
            tooltip.replaceChildren(...texts.map(text => {
                const paragraph = document.createElement("p");
                paragraph.textContent = text;
                return paragraph;
            }));
        }
        tooltip.hidden = false;
        place(anchor, x ?? (anchor.left + anchor.right) / 2);
    }

    function hide() {
        clearTimeout(timer);
        timer = 0;
        owner = null;
        if (element) element.hidden = true;
    }

    // The text an element's tooltip shows, taking over its title if it still has one.
    function textOf(target) {
        const title = target.getAttribute("title");
        if (title) {
            target.dataset.tooltip = title;
            target.removeAttribute("title");
            if (!target.hasAttribute("aria-label") && !target.textContent.trim()) target.setAttribute("aria-label", title);
        }
        return target.dataset.tooltip || "";
    }

    document.addEventListener("pointerover", event => {
        const target = event.target instanceof Element ? event.target.closest("[title], [data-tooltip]") : null;
        if (target === owner) return;
        // Only an element's own tooltip ends here; a custom one, such as a comment's, is its
        // caller's to hide, or it would flicker whenever the pointer crossed an inline element.
        if (owner) hide();
        if (!target) return;
        const text = textOf(target);
        if (!text) return;
        owner = target;
        timer = setTimeout(() => {
            if (owner !== target || !target.isConnected) return;
            // One paragraph: line breaks in a title are kept, the divider is for separate comments.
            show([text], target.getBoundingClientRect(), null, `title:${text}`);
        }, SHOW_DELAY_MS);
    }, { passive: true });
    document.addEventListener("pointerout", event => {
        if (owner && !(event.relatedTarget instanceof Node && owner.contains(event.relatedTarget))) hide();
    }, { passive: true });
    document.addEventListener("pointerdown", hide, true);
    document.addEventListener("keydown", hide, true);
    document.addEventListener("scroll", hide, { capture: true, passive: true });
    window.addEventListener("blur", hide);

    window.appTooltip = {
        // For tooltips that belong to no element, such as a comment highlighted in message text.
        show: (texts, anchor, x) => {
            clearTimeout(timer);
            owner = null;
            show(texts, anchor, x, `custom:${texts.join("\n\n")}`);
        },
        hide
    };
})();
