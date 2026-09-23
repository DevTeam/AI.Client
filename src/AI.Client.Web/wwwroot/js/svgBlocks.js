// Preview complete SVG code fences as images. Markdown keeps raw HTML disabled;
// the SVG source is used only as an image URL, never inserted into the page DOM.
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
        if (pre.closest('.svg-block')) continue;
        const source = sourceOf(pre);
        if (!isSvg(source)) continue;

        const wrapper = document.createElement('div');
        wrapper.className = 'svg-block';
        const image = document.createElement('img');
        image.alt = 'SVG image';
        image.loading = 'lazy';
        image.src = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(source)}`;
        wrapper.appendChild(image);
        pre.replaceWith(wrapper);
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
    observer.observe(container, { childList: true, subtree: true, characterData: true });
    schedule();
    return { dispose: () => { disposed = true; observer.disconnect(); } };
}
