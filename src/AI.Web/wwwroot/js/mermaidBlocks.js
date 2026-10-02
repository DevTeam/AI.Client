// Renders ```mermaid``` code fences inside the message feed as live SVG diagrams.
//
// Markdig (with `UseAdvancedExtensions` and `DisableHtml`) already turns a fenced block into
// `<pre><code class="language-mermaid">…</code></pre>`. Mermaid itself does not auto-discover those
// blocks — it walks the DOM once when `mermaid.run()` is called and never looks again. The
// message feed here is a long-lived Blazor container with content that streams in token by
// token, so we attach a MutationObserver to it and re-render every new (or newly edited) fence.
//
// A render is identified by the SHA-256 of its source, hex-truncated to sixteen characters. That
// is enough to survive Blazor re-creating a fence with the same code after a re-render — the
// replacement carries the same id and `data-mermaid-state` attribute, the observer sees it, and
// the second pass is a no-op. A genuinely different code, even by one character, picks a fresh
// id and renders fresh. The id also drives the `key` Mermaid hands back, which keeps mermaid's
// own id-counter and ours from colliding across long transcripts.
//
// Each fence ends up as one of:
//   <div class="mermaid-block" data-mermaid-state="ok">…zoom toolbar…<div class="mermaid-svg">…SVG…</div></div>
//   <div class="mermaid-block" data-mermaid-state="error">…error message…<pre>code</pre></div>
// The original `<pre><code>` is hidden via CSS when `data-mermaid-state` is set, so a re-render
// that replaces the node keeps its identity (the same element, same data-attribute, just the
// contents inside have changed) and does not flash the raw source at the user.
//
// Errors stay visible on purpose: a model that produced broken mermaid needs to see the
// diagnostic so its next response can fix it. The error block also preserves the source so a
// human can copy it back to the editor without going through the chat history.
//
// A fence inside a message that is still being written is not rendered at all. Markdown closes an
// unterminated fence at the end of the text, so while the answer streams, the feed holds a
// ```mermaid block that is missing everything the model has not typed yet. Rendering it hands
// mermaid half a diagram, and the reader gets a parse error mid-answer that disappears again a
// second later — noise, not a diagnostic, and it also re-renders the same source once per token.
// Such fences are skipped until the message stops streaming; the `streaming` class leaves the
// message element at that moment, which is itself a DOM mutation that brings the pass back.

const STATE_ATTR = "data-mermaid-state";
const ID_ATTR = "data-mermaid-id";
const RENDERED_CLASS = "mermaid-block";

// Marked on the message element by the feed while the turn is still producing text (see
// MessageFeed.razor: the live-text and streaming articles both carry it).
const STREAMING_CLASS = "streaming";

const isStreaming = node => node.closest(`.${STREAMING_CLASS}`) !== null;

// SHA-256 of the source, hex. Used as the diagram id so re-renders of identical code are
// idempotent. SubtleCrypto is available in every browser a Blazor WASM app runs in.
const idForSource = async source => {
    const bytes = new TextEncoder().encode(source);
    const digest = await crypto.subtle.digest("SHA-256", bytes);
    return Array.from(new Uint8Array(digest))
        .map(b => b.toString(16).padStart(2, "0"))
        .join("")
        .slice(0, 16);
};

// Markdig with UseAdvancedExtensions renders a ```mermaid fence in one of two shapes
// depending on the active extension pipeline:
//   1. `<pre><code class="language-mermaid">graph TD …</code></pre>` (the documented form);
//   2. `<pre class="mermaid">graph TD …</pre>` with no inner <code> — observed in the live
//      AI feed, where the language class is hoisted onto the <pre>.
// The selector list below covers both, plus a couple of variants we have seen in other
// Markdig-based pipelines. The renderer below also tolerates a <pre> that contains no <code>
// child — it falls back to the <pre>'s own text content.
const FENCE_SELECTORS = [
    'pre > code.language-mermaid',
    'pre > code.lang-mermaid',
    'pre.mermaid > code',
    'pre.mermaid',                                 // ← live AI shape
    'pre > code[data-language="mermaid"]',
    'pre > code[class*="language-mermaid"]',
    'pre > code[class*="lang-mermaid"]'
];

// Given a node that matched one of FENCE_SELECTORS, return the element whose textContent
// holds the mermaid source. For `<pre><code>` shapes that is the <code>; for the
// `<pre class="mermaid">` shape that is the <pre> itself, because there is no inner <code>.
const sourceNode = node => {
    if (!node) return null;
    if (node.tagName === 'CODE') return node;
    if (node.tagName === 'PRE') {
        const inner = node.querySelector('code');
        return inner || node;
    }
    return node;
};

const findFences = root => {
    if (!root) return [];
    const found = new Set();
    for (const selector of FENCE_SELECTORS) {
        for (const node of root.querySelectorAll(selector)) {
            // Replace the whole fence. Replacing only <code> leaves a <div> inside <pre>,
            // which inherits code-block layout and may stretch the chat column.
            found.add(node.tagName === "PRE" ? node : node.closest("pre") ?? node);
        }
    }
    return Array.from(found);
};

// Pull the code text out of a Markdig fence. Markdig HTML-escapes the source as part of its
// sanitization pass (`DisableHtml`), so we have to unescape before handing it to mermaid —
// otherwise a `&lt;` shows up as text inside the diagram.
const unescape = html => {
    const template = document.createElement("textarea");
    template.innerHTML = html;
    return template.value;
};

const wrap = (original, state, content) => {
    const wrapper = document.createElement("div");
    wrapper.className = RENDERED_CLASS;
    wrapper.setAttribute(STATE_ATTR, state);
    // carry the id over so a second observer pass for the same source is a no-op
    if (original.hasAttribute(ID_ATTR)) {
        wrapper.setAttribute(ID_ATTR, original.getAttribute(ID_ATTR));
    }
    wrapper.appendChild(content);
    original.replaceWith(wrapper);
    return wrapper;
};

// Zoom controls for a rendered diagram. The fence itself is replaced by this module, so the buttons
// are built here rather than in Razor; they land inside the same block as the SVG and take the
// chat's --color-* tokens through CSS (see app.css). Only the ok state gets them: an error block is
// a diagnostic to read, not a diagram to look at closely.
//
// The buttons are inert markup until the delegated click handler in attach() sees them, which keeps
// them working after Blazor re-creates the markup around them.
const ZOOM_ATTR = "data-mermaid-zoom";
const ZOOM_ACTION_ATTR = "data-mermaid-zoom-action";
const ZOOM_BASE_STYLE_ATTR = "data-mermaid-base-style";
const ZOOM_MIN = 0.5;
const ZOOM_MAX = 4;
const ZOOM_STEP = 0.25;

// The icon paths follow the 24×24 stroked grid of AppIcon.razor; "reset" is the same circular
// arrow the app already uses for "back to the default".
const ZOOM_ACTIONS = [
    { action: "out", title: "Zoom out", icon: '<path d="M5 12h14" />' },
    { action: "in", title: "Zoom in", icon: '<path d="M12 5v14M5 12h14" />' },
    {
        action: "reset",
        title: "Reset zoom",
        icon: '<path d="M3 12a9 9 0 1 0 3-6.7L3 8" /><path d="M3 3v5h5" />'
    }
];

const zoomButton = ({ action, title, icon }) => {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "mermaid-zoom-button";
    button.setAttribute(ZOOM_ACTION_ATTR, action);
    // title drives the app's own tooltip (js/tooltips.js); aria-label is the accessible name.
    button.setAttribute("title", title);
    button.setAttribute("aria-label", title);
    button.innerHTML =
        '<svg class="mermaid-zoom-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
        `stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${icon}</svg>`;
    return button;
};

const zoomToolbar = () => {
    const toolbar = document.createElement("div");
    toolbar.className = "mermaid-toolbar";
    toolbar.setAttribute("role", "group");
    toolbar.setAttribute("aria-label", "Diagram zoom");

    // The percentage is a plain label, not a control: it reports the state the two buttons set.
    const level = document.createElement("span");
    level.className = "mermaid-zoom-level";
    level.textContent = "100%";

    toolbar.append(zoomButton(ZOOM_ACTIONS[0]), level, zoomButton(ZOOM_ACTIONS[1]), zoomButton(ZOOM_ACTIONS[2]));
    return toolbar;
};

// Mermaid hands back an SVG whose own inline `style` carries a `max-width` derived from the
// diagram's natural size — that is what keeps a two-node flowchart from being stretched across the
// whole column. The pristine value is kept on the block and written back for every zoom, so 100%
// restores the diagram exactly as mermaid drew it, however the block was zoomed before.
const zoomBaseStyle = (block, svg) => {
    const stored = block.getAttribute(ZOOM_BASE_STYLE_ATTR);
    if (stored !== null) return stored;
    const style = svg.getAttribute("style") ?? "";
    block.setAttribute(ZOOM_BASE_STYLE_ATTR, style);
    return style;
};

// Zoom is a multiple of the diagram's fitted size — the size 100% shows — so the same percentage
// means the same picture whatever the width of the chat column or the natural size of the diagram.
// The size is applied as width/height in px on the SVG, not as a transform: scale() leaves the
// layout box untouched, so a zoomed-in diagram would overlap the messages below it and the block
// would not scroll to its edges.
//
// The fitted size is measured with mermaid's own style put back first, every time. Measuring the
// already-zoomed box would compound each step on the previous one, and a stored measurement would
// go stale as soon as the chat column changes width.
const applyZoom = (block, requested) => {
    const value = Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, Math.round(requested / ZOOM_STEP) * ZOOM_STEP));
    block.setAttribute(ZOOM_ATTR, String(value));

    const svg = block.querySelector(".mermaid-svg svg");
    if (svg) {
        const base = zoomBaseStyle(block, svg);
        // Put the diagram back to its fitted style before measuring, whatever it showed a moment ago.
        svg.setAttribute("style", base);
        const fitted = svg.getBoundingClientRect();
        if (value !== 1 && fitted.width > 0 && fitted.height > 0) {
            // Keep both dimensions proportional even if the holder's flex layout would shrink
            // the width to fit. Otherwise only the viewport height grows past the column width.
            const size = `flex-shrink: 0; max-width: none; width: ${fitted.width * value}px; height: ${fitted.height * value}px;`;
            svg.setAttribute("style", base.length === 0 || base.trimEnd().endsWith(";") ? `${base} ${size}` : `${base}; ${size}`);
        }
    }

    const level = block.querySelector(".mermaid-zoom-level");
    if (level) level.textContent = `${Math.round(value * 100)}%`;

    // Both ends of the range are reachable and then hold: a button that cannot do anything says so
    // instead of quietly ignoring the click.
    for (const button of block.querySelectorAll(`[${ZOOM_ACTION_ATTR}]`)) {
        const action = button.getAttribute(ZOOM_ACTION_ATTR);
        button.disabled = (action === "out" && value <= ZOOM_MIN) || (action === "in" && value >= ZOOM_MAX);
    }
};

const zoomBy = (block, action) => {
    const current = Number(block.getAttribute(ZOOM_ATTR)) || 1;
    if (action === "in") applyZoom(block, current + ZOOM_STEP);
    else if (action === "out") applyZoom(block, current - ZOOM_STEP);
    else if (action === "reset") applyZoom(block, 1);
};

// Render a single fence. Returns the wrapper element on success, or an error wrapper on failure.
// Idempotent: re-running on a wrapper that already has data-mermaid-state returns it as-is.
const renderOne = async matchedNode => {
    if (matchedNode.hasAttribute(STATE_ATTR)) return matchedNode.parentElement;
    const sourceEl = sourceNode(matchedNode);
    if (!window.mermaid || typeof window.mermaid.render !== "function") {
        return wrap(matchedNode, "error", errorBlock("Mermaid is not loaded.", sourceEl ? sourceEl.textContent ?? "" : ""));
    }

    const source = sourceEl ? unescape(sourceEl.textContent ?? "").trim() : "";
    if (source.length === 0) {
        return wrap(matchedNode, "error", errorBlock("Empty mermaid block.", ""));
    }

    const id = await idForSource(source);
    matchedNode.setAttribute(ID_ATTR, id);

    let svg;
    try {
        // mermaid.render resolves with `{ svg }` and inserts the diagram under the id it was
        // given. We pass a DOM id that we own (per-source hash) to keep two diagrams from
        // fighting for the same temporary element.
        const result = await window.mermaid.render(`mermaid-${id}`, source);
        svg = result.svg;
    } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        return wrap(matchedNode, "error", errorBlock(message, source));
    }

    const holder = document.createElement("div");
    holder.className = "mermaid-svg";
    holder.innerHTML = svg;

    const content = document.createDocumentFragment();
    content.append(zoomToolbar(), holder);
    const block = wrap(matchedNode, "ok", content);
    // Puts the block at 100%: it records mermaid's own style for later zooms and settles the
    // buttons' enabled state.
    applyZoom(block, 1);
    return block;
};

const errorBlock = (message, source) => {
    const root = document.createElement("div");
    root.className = "mermaid-error";

    const header = document.createElement("div");
    header.className = "mermaid-error-header";
    header.textContent = "Mermaid could not render this diagram.";
    root.appendChild(header);

    const detail = document.createElement("p");
    detail.className = "mermaid-error-message";
    detail.textContent = message;
    root.appendChild(detail);

    if (source) {
        const code = document.createElement("pre");
        code.className = "mermaid-source";
        code.textContent = source;
        root.appendChild(code);
    }

    return root;
};

// Walk every fence under `root` and render any that have not been processed yet. We render in
// parallel — mermaid's render() is async and the loop awaits each individually so a long diagram
// does not block shorter ones behind it, but ordering is preserved because the wrap() call
// happens right after the render resolves for each item.
const renderAll = async root => {
    const fences = Array.from(findFences(root));
    if (fences.length === 0) return;

    // Filter out fences whose parent wrapper already carries a state — these are wrappers that
    // were re-mounted by Blazor with the same id but a fresh inner structure. renderOne skips
    // them, but skipping here avoids even the attribute check on the hot path.
    // A fence in a still-streaming message waits for the turn to finish (see the note on
    // STREAMING_CLASS at the top). It is left untouched rather than wrapped in an error state, so
    // the observer's next pass — which the class removal triggers — sees a pristine fence.
    const pending = fences.filter(code =>
        !code.closest(`.${RENDERED_CLASS}`) && !isStreaming(code));

    // Sequential, not parallel. mermaid.render mutates a shared registry under the hood and the
    // official guidance is one render at a time; doing it in series also keeps memory bounded
    // for a transcript that streams in dozens of blocks.
    for (const code of pending) {
        // Re-check inside the loop: a previous iteration may have wrapped a sibling of this
        // node, which moves our DOM anchor. querySelectorAll above took a snapshot.
        if (!code.isConnected) continue;
        try {
            await renderOne(code);
        } catch (error) {
            // Defensive: renderOne already catches mermaid's own errors. Anything that escapes
            // here is unexpected but must not break the rest of the pass.
            const message = error instanceof Error ? error.message : String(error);
            wrap(code, "error", errorBlock(`Unexpected: ${message}`, code.textContent ?? ""));
        }
    }
};

// One delegated listener for every toolbar in the feed. Buttons appear and disappear as Blazor
// diffs the transcript, so nothing is bound per button; the handler walks up to the block instead.
const onClick = event => {
    const button = event.target instanceof Element ? event.target.closest(`[${ZOOM_ACTION_ATTR}]`) : null;
    if (!button) return;
    const block = button.closest(`.${RENDERED_CLASS}`);
    if (!block) return;
    zoomBy(block, button.getAttribute(ZOOM_ACTION_ATTR));
};

export function attach(container) {
    let scheduled = false;
    let observer = null;

    // MutationObserver already coalesces into microtask checkpoints, but Blazor's diff can
    // produce a burst of mutations for a single streaming token. Deferring with rAF gives the
    // browser one paint cycle to settle the DOM before we measure it, which keeps the feed
    // responsive while a long assistant answer is still being written.
    const schedule = () => {
        if (scheduled) return;
        scheduled = true;
        requestAnimationFrame(() => {
            scheduled = false;
            renderAll(container).catch(error => {
                // Defensive: renderOne already catches mermaid's own errors. Anything that
                // escapes here is unexpected but must not crash the observer or stop further
                // updates. We log so the developer sees it in DevTools and let the next
                // mutation pick up where we left off.
                console.error("mermaidBlocks.renderAll failed:", error);
            });
        });
    };

    observer = new MutationObserver(schedule);
    // Class attributes are watched as well: a fence skipped while its message was streaming has
    // to be picked up when that message stops, and the only mutation marking that is the removal
    // of `streaming` from the article above it (the fence text itself does not change again).
    observer.observe(container, {
        childList: true,
        subtree: true,
        characterData: true,
        attributes: true,
        attributeFilter: ["class"]
    });

    container.addEventListener("click", onClick);

    // Render whatever was already in the feed when we attached — turning on mermaid in the
    // middle of a session must not require the user to scroll back to the top.
    schedule();

    return {
        dispose: () => {
            if (observer) {
                observer.disconnect();
                observer = null;
            }
            container.removeEventListener("click", onClick);
            scheduled = false;
        }
    };
}
