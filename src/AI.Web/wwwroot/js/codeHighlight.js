// Highlight completed Markdown code fences. The library is loaded only when a chat contains code.
const libraryUrl = new URL('./vendor/highlight.min.js', import.meta.url).href;
let libraryPromise;
const extraLanguages = {
    powershell: 'powershell', pwsh: 'powershell', ps: 'powershell', ps1: 'powershell',
    fsharp: 'fsharp', fs: 'fsharp', 'f#': 'fsharp',
    dockerfile: 'dockerfile', docker: 'dockerfile'
};
const languagePromises = new Map();

const loadScript = url => new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = url;
    script.onload = resolve;
    script.onerror = () => reject(new Error(`Could not load ${url}`));
    document.head.appendChild(script);
});

const loadLibrary = () => libraryPromise ??= (window.hljs
    ? Promise.resolve(window.hljs)
    : loadScript(libraryUrl).then(() => window.hljs).catch(error => {
        libraryPromise = undefined;
        throw error;
    }));

const loadLanguage = async (hljs, language) => {
    if (hljs.getLanguage(language)) return true;
    const extra = extraLanguages[language];
    if (!extra) return false;
    if (!languagePromises.has(extra)) {
        const url = new URL(`./vendor/${extra}.min.js`, import.meta.url).href;
        languagePromises.set(extra, loadScript(url).catch(error => {
            languagePromises.delete(extra);
            throw error;
        }));
    }
    await languagePromises.get(extra);
    return !!hljs.getLanguage(language);
};

const languageOf = element => {
    if (!element) return null;
    for (const name of element.classList) {
        const match = /^(?:language-|lang-)(.+)$/.exec(name);
        if (match) return match[1].toLowerCase();
    }
    // Markdig's advanced pipeline also emits <pre class="csharp"><code>...</code></pre>.
    if (element.tagName === 'PRE') return element.classList[0]?.toLowerCase() ?? null;
    return null;
};

const highlightAll = async container => {
    const blocks = [...container.querySelectorAll('.markdown-content pre')].filter(pre => {
        if (pre.dataset.codeHighlighted || pre.closest('.mermaid-block, .svg-block')) return false;
        const language = languageOf(pre.firstElementChild) ?? languageOf(pre);
        return language !== 'mermaid' && language !== 'svg' && !pre.closest('.streaming');
    });
    if (blocks.length === 0) return;

    const hljs = await loadLibrary();
    for (const pre of blocks) {
        if (!pre.isConnected || pre.dataset.codeHighlighted || pre.closest('.streaming, .mermaid-block, .svg-block')) continue;
        let code = pre.querySelector(':scope > code');
        const source = (code ?? pre).textContent ?? '';
        const language = languageOf(code) ?? languageOf(pre);
        // Unknown language labels leave the original code intact. Unlabelled blocks use detection.
        const result = language
            ? (await loadLanguage(hljs, language) ? hljs.highlight(source, { language, ignoreIllegals: true }) : null)
            : hljs.highlightAuto(source);
        pre.dataset.codeHighlighted = 'true';
        if (!result) continue;
        if (!code) {
            code = document.createElement('code');
            pre.replaceChildren(code);
        }
        code.innerHTML = result.value;
        code.classList.add('hljs');
    }
};

export async function highlightText(text, language) {
    const hljs = await loadLibrary();
    return await loadLanguage(hljs, language) ? hljs.highlight(text, { language, ignoreIllegals: true }).value : null;
}

export function attach(container) {
    let scheduled = false;
    let disposed = false;
    const schedule = () => {
        if (scheduled || disposed) return;
        scheduled = true;
        requestAnimationFrame(() => {
            scheduled = false;
            if (!disposed) highlightAll(container).catch(error => console.error('Code highlighting failed:', error));
        });
    };
    const observer = new MutationObserver(schedule);
    observer.observe(container, {
        childList: true, subtree: true, characterData: true,
        attributes: true, attributeFilter: ['class']
    });
    schedule();
    return { dispose: () => { disposed = true; observer.disconnect(); } };
}
