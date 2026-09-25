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
//   <div class="mermaid-block" data-mermaid-state="ok">…SVG…</div>
//   <div class="mermaid-block" data-mermaid-state="error">…error message…<pre>code</pre></div>
// The original `<pre><code>` is hidden via CSS when `data-mermaid-state` is set, so a re-render
// that replaces the node keeps its identity (the same element, same data-attribute, just the
// contents inside have changed) and does not flash the raw source at the user.
//
// Errors stay visible on purpose: a model that produced broken mermaid needs to see the
// diagnostic so its next response can fix it. The error block also preserves the source so a
// human can copy it back to the editor without going through the chat history.

const STATE_ATTR = "data-mermaid-state";
const ID_ATTR = "data-mermaid-id";
const RENDERED_CLASS = "mermaid-block";

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
//      AI.Client feed, where the language class is hoisted onto the <pre>.
// The selector list below covers both, plus a couple of variants we have seen in other
// Markdig-based pipelines. The renderer below also tolerates a <pre> that contains no <code>
// child — it falls back to the <pre>'s own text content.
const FENCE_SELECTORS = [
    'pre > code.language-mermaid',
    'pre > code.lang-mermaid',
    'pre.mermaid > code',
    'pre.mermaid',                                 // ← live AI.Client shape
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
    return wrap(matchedNode, "ok", holder);
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
    const pending = fences.filter(code => !code.closest(`.${RENDERED_CLASS}`));

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
    observer.observe(container, { childList: true, subtree: true, characterData: true });

    // Render whatever was already in the feed when we attached — turning on mermaid in the
    // middle of a session must not require the user to scroll back to the top.
    schedule();

    return {
        dispose: () => {
            if (observer) {
                observer.disconnect();
                observer = null;
            }
            scheduled = false;
        }
    };
}
