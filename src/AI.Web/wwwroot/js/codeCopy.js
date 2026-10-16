(() => {
    // Every Markdown code fence — chat messages, the file preview, the review text — gets a Copy
    // button in its corner. One observer on the document covers them all, whichever component
    // rendered the Markdown.
    //
    // The <pre> belongs to Blazor (see svgBlocks.js): it is a top-level node of a MarkupString and
    // must stay where it is, so the button goes inside it. The <pre> holds the button still while
    // its <code> scrolls sideways (app.css). The button carries `message-copy-action`, so
    // messageActions.js shows the same check as the message's own Copy once it is clicked.
    const READY_ATTR = "data-code-copy";
    // Diagram and image fences are replaced by their rendering; the fence source is not what the
    // reader sees there.
    const RENDERED_LANGUAGES = new Set(["mermaid", "svg"]);

    const languageOf = element => {
        if (!element) return null;
        for (const name of element.classList) {
            const match = /^(?:language-|lang-)(.+)$/.exec(name);
            if (match) return match[1].toLowerCase();
        }
        return element.tagName === "PRE" ? element.classList[0]?.toLowerCase() ?? null : null;
    };

    const icon = (paths, extraClass) =>
        `<svg class="app-icon${extraClass ? ` ${extraClass}` : ""}" viewBox="0 0 24 24" fill="none" stroke="currentColor" ` +
        `stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${paths}</svg>`;

    // The same glyphs as AppIcon.razor's "copy" and "check".
    const BUTTON_HTML =
        icon('<rect x="9" y="9" width="11" height="11" rx="2" /><path d="M15 9V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v7a2 2 0 0 0 2 2h3" />') +
        icon('<path d="m5 12 4 4L19 6" />', "copied-icon");

    const wantsButton = pre => {
        if (pre.hasAttribute(READY_ATTR)) return false;
        // A fence still being written changes with every token; it gets its button when it is done.
        if (pre.closest(".streaming, .mermaid-block, .svg-block")) return false;
        const code = pre.querySelector(":scope > code");
        const language = languageOf(code) ?? languageOf(pre);
        return !RENDERED_LANGUAGES.has(language);
    };

    const addButtons = () => {
        for (const pre of document.querySelectorAll(".markdown-content pre")) {
            if (!wantsButton(pre)) continue;
            const button = document.createElement("button");
            button.type = "button";
            button.className = "icon-action code-copy-action message-copy-action";
            button.title = "Copy code";
            button.setAttribute("aria-label", "Copy code");
            button.innerHTML = BUTTON_HTML;
            pre.setAttribute(READY_ATTR, "");
            pre.prepend(button);
        }
    };

    // The code without the button and without the line break Markdown ends every fence with.
    const sourceOf = pre => {
        const code = pre.querySelector(":scope > code");
        const text = code ? code.textContent ?? "" : [...pre.childNodes]
            .filter(node => !(node instanceof Element && node.classList.contains("code-copy-action")))
            .map(node => node.textContent ?? "").join("");
        return text.replace(/\r?\n$/, "");
    };

    document.addEventListener("click", event => {
        const button = event.target instanceof Element ? event.target.closest(".code-copy-action") : null;
        const pre = button?.closest("pre");
        if (!pre) return;
        // The check messageActions.js shows would claim a copy that did not happen.
        navigator.clipboard.writeText(sourceOf(pre)).catch(error => {
            button.classList.remove("is-copied");
            console.error("Could not copy code:", error);
        });
    });

    let scheduled = false;
    const schedule = () => {
        if (scheduled) return;
        scheduled = true;
        requestAnimationFrame(() => {
            scheduled = false;
            addButtons();
        });
    };

    // Class changes count: a fence skipped while its message streamed is picked up when the
    // `streaming` class leaves the message.
    const start = () => {
        new MutationObserver(schedule).observe(document.body, {
            childList: true, subtree: true, attributes: true, attributeFilter: ["class"]
        });
        schedule();
    };
    if (document.body) start();
    else document.addEventListener("DOMContentLoaded", start);
})();
