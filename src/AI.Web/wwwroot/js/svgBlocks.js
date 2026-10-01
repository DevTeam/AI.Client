// Preview complete SVG code fences as images. Markdown keeps raw HTML disabled;
// the SVG source is used only as an image URL, never inserted into the page DOM.
//
// The fence's <pre> belongs to Blazor: it is a top-level node of the message's MarkupString, and
// the next render that changes the markup removes it by reference. Replacing it (replaceWith)
// detaches that node, and Blazor then fails with "Cannot read properties of null (reading
// 'removeChild')". So the <pre> stays where it is: it gets the `svg-block` class, the image goes
// inside it, and CSS hides the source <code>. When Blazor drops the <pre>, the preview goes with it.
//
// A fence in a message that is still being written is skipped, as in mermaidBlocks.js: every token
// re-creates the markup, so a preview built mid-stream would be thrown away and rebuilt per token.
// The `streaming` class leaving the message is a mutation that brings the pass back.
const RENDERED_CLASS = 'svg-block';
const STREAMING_CLASS = 'streaming';

const languageOf = element => {
    for (const token of element.classList) {
        const match = /^(?:language-|lang-)?(svg|xml|php-template)$/.exec(token);
        if (match) return match[1];
    }
    return null;
};

const sourceOf = pre => {
    const code = pre.querySelector(':scope > code');
    if (!languageOf(code ?? pre) && !languageOf(pre)) return null;
    return (code ?? pre).textContent?.trim() ?? '';
};

const isSvg = source => {
    if (!source || /<!DOCTYPE/i.test(source)) return false;
    const document = new DOMParser().parseFromString(source, 'image/svg+xml');
    return document.documentElement.localName === 'svg'
        && document.documentElement.namespaceURI === 'http://www.w3.org/2000/svg'
        && !document.querySelector('parsererror');
};

const renderAll = container => {
    for (const pre of container.querySelectorAll('pre')) {
        if (pre.closest(`.${RENDERED_CLASS}, .${STREAMING_CLASS}`)) continue;
        const source = sourceOf(pre);
        if (!isSvg(source)) continue;

        const image = document.createElement('img');
        image.alt = 'SVG image';
        image.loading = 'lazy';
        image.src = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(source)}`;
        // Markdig may hoist the language onto a bare <pre>; give the source a <code> so CSS hides it.
        if (!pre.querySelector(':scope > code')) {
            const code = document.createElement('code');
            code.append(...pre.childNodes);
            pre.append(code);
        }
        pre.classList.add(RENDERED_CLASS);
        pre.prepend(image);
    }
};

export function attach(container) {
    let scheduled = false;
    let disposed = false;
    const schedule = () => {
        if (scheduled || disposed) return;
        scheduled = true;
        requestAnimationFrame(() => {
            scheduled = false;
            if (!disposed) renderAll(container);
        });
    };
    const observer = new MutationObserver(schedule);
    observer.observe(container, {
        childList: true,
        subtree: true,
        characterData: true,
        attributes: true,
        attributeFilter: ['class']
    });
    schedule();
    return { dispose: () => { disposed = true; observer.disconnect(); } };
}
