// Turns paths in rendered messages into links to local files and directories, however the model
// wrote them: a file URI or bare Windows path in a link, a relative link like [x](src/y.cs), a code
// span like `src/Program.cs:12`, or a bare C:\ path in the text. Only file URIs and Windows paths
// in links are recognised on sight; everything else becomes a link once the Host has confirmed
// that the path exists and the project may read it.
//
// The transcript must not get slower for it:
// - only message blocks that changed are scanned, on a short timer and in slices of a few
//   milliseconds, so a streaming answer is looked at a few times a second at most, never per
//   token, and a long transcript never holds the main thread (idle callbacks were not used: a
//   hidden or background page runs them only on their timeout, one block at a time);
// - candidates are cheap string tests; the file system is asked only through the Host, in batches,
//   with a plain fetch: marshalling a batch through .NET cost the WebAssembly runtime ~150 ms;
// - every answer is cached per project, so re-rendered text is decorated without asking again.
// A right-click on a recognised path hands it to .NET, which offers to add it to the next message.

const BLOCK = '.markdown-content';
const SCAN_DELAY_MS = 200;
const FLUSH_DELAY_MS = 120;
const BATCH_SIZE = 100;
const SLICE_MS = 8;
// Text in these is not scanned for bare paths: code has its own rule, and the rest is not prose.
const NOT_PROSE = 'pre, code, a, .mermaid-block, .svg-block, .file-link';
const LINE_SUFFIX = /(?:#L\d+(?:-L?\d+)?|:\d+(?::\d+)?)$/;
const WINDOWS_PATH = /[A-Za-z]:\\[^\s<>"'|*?`]+/g;

// The local path a link names on sight — a file URI or a Windows path — or null.
const directPathOf = href => {
    if (/^file:/i.test(href)) {
        try {
            const url = new URL(href);
            const path = decodeURIComponent(url.pathname);
            if (url.host) return `\\\\${url.host}${path.replace(/\//g, '\\')}`;
            // file:///C:/repo → C:\repo; file:///home/me stays a POSIX path.
            if (/^\/[A-Za-z]:/.test(path)) return path.slice(1).replace(/\//g, '\\');
            return path || null;
        } catch {
            return null;
        }
    }
    if (/^[A-Za-z]:(\\|\/|%5C)/i.test(href)) {
        try {
            return decodeURIComponent(href);
        } catch {
            return href;
        }
    }
    return null;
};

// A link with no scheme and no fragment-only target, such as src/app.cs or ../docs.
const relativeTargetOf = href => {
    if (!href || href.startsWith('#') || href.startsWith('//') || /^[a-z][a-z0-9+.-]*:/i.test(href)) return null;
    try {
        return decodeURIComponent(href);
    } catch {
        return href;
    }
};

// Whether a code span reads like a path. Wrong guesses cost one cached lookup, so this errs
// towards asking, but it keeps calls, URLs, expressions and namespaces out.
const looksLikePath = text => {
    if (text.length < 3 || text.length > 400 || /[\r\n<>"|*?]/.test(text)) return false;
    if (/^[a-z][a-z0-9+.-]*:\/\//i.test(text)) return false;
    if (/^[A-Za-z]:[\\/]/.test(text)) return true;
    if (/\s|[(){}=;,$]/.test(text)) return false;
    const bare = text.replace(LINE_SUFFIX, '');
    if (/[\\/]/.test(bare)) return /\w/.test(bare) && !bare.startsWith('//');
    if (!/^[\w.-]+\.[A-Za-z0-9]*[A-Za-z][A-Za-z0-9]*$/.test(bare)) return false;
    // System.Text.Json is a namespace, appsettings.Development.json a file.
    const segments = bare.split('.');
    return segments.length < 3 || !segments.every(segment => /^[A-Z]/.test(segment));
};

// The Windows paths in one text node, without the punctuation a sentence puts after them.
const windowsPathsIn = text => {
    if (text.indexOf(':\\') < 1) return [];
    const found = [];
    for (const match of text.matchAll(WINDOWS_PATH)) {
        const path = match[0].replace(/[.,;:!?)\]]+$/, '');
        if (path.length > 3) found.push({ index: match.index, path });
    }
    return found;
};

// The Host spells its enums as numbers unless told otherwise.
const kindName = kind => kind === 0 || kind === 'File' ? 'File' : kind === 1 || kind === 'Directory' ? 'Directory' : null;
const accessName = access => access === 2 || access === 'ReadWrite' ? 'write'
    : access === 1 || access === 'Read' ? 'read' : 'none';
const ACCESS_TITLES = {
    read: 'The project can read this; right-click to add it to the message',
    write: 'The project can read and change this; right-click to add it to the message',
    none: 'The project cannot read this yet; right-click to allow access'
};

export function attach(container, dotnet) {
    let scope = null;
    let resolveUrl = null;
    let disposed = false;
    // input → { path, kind, access } for a path the Host found, null for one it did not. access is
    // 'read', 'write' or 'none' (outside the project's directories), or null before the Host answered.
    const cache = new Map();
    const pending = new Set();
    const inFlight = new Set();
    const dirty = new Set();
    let scanTimer = 0;
    let flushTimer = 0;

    const decorate = (element, resolved) => {
        element.classList.add('file-link');
        // chatComposer.js drags every [data-file-path] as that path, like any other in the app.
        element.draggable = true;
        element.dataset.filePath = resolved.path;
        if (resolved.kind) element.dataset.fileKind = resolved.kind;
        else delete element.dataset.fileKind;
        // The access shows as an icon after the link, drawn by CSS from this attribute.
        if (resolved.access) element.dataset.fileAccess = resolved.access;
        else delete element.dataset.fileAccess;
        element.title = `${ownHintOf(element) ?? resolved.path}\n${ACCESS_TITLES[resolved.access] ?? 'Right-click to add it to the message'}`;
    };

    // An "@" link comes with its own hint — the token as written and the path — which the access
    // line is added to rather than replacing it. tooltips.js may already have moved it.
    const ownHintOf = element => {
        if (!element.classList.contains('mention-link')) return null;
        element.dataset.mentionHint ??= element.getAttribute('title') ?? element.dataset.tooltip ?? '';
        return element.dataset.mentionHint || null;
    };

    // Decorates an element from the cache, or queues its path and marks it pending.
    // A verdict can turn negative once a directory is removed from the project, so an element that
    // was a link stops being one — except a link written as a local path, which stays usable as written.
    const undecorate = element => {
        const direct = element.matches('a[href]') ? directPathOf(element.getAttribute('href') ?? '') : null;
        if (direct) {
            decorate(element, { path: direct, kind: null, access: null });
            return;
        }
        element.classList.remove('file-link');
        element.removeAttribute('draggable');
        delete element.dataset.filePath;
        delete element.dataset.fileKind;
        delete element.dataset.fileAccess;
        element.removeAttribute('title');
        // tooltips.js may already have moved the title here.
        delete element.dataset.tooltip;
    };

    const consider = (element, input) => {
        if (cache.has(input)) {
            const resolved = cache.get(input);
            element.dataset.pathState = resolved ? 'yes' : 'no';
            element.dataset.pathInput = input;
            if (resolved) decorate(element, resolved);
            else if (element.dataset.filePath) undecorate(element);
            return;
        }
        element.dataset.pathState = 'pending';
        element.dataset.pathInput = input;
        queue(input);
    };

    const scanBlock = block => {
        if (!block.isConnected) return;
        for (const anchor of block.querySelectorAll('a[href]:not([data-path-state])')) {
            const href = anchor.getAttribute('href') ?? '';
            const direct = directPathOf(href);
            if (direct) {
                // Usable at once; the Host's answer only adds the kind and the canonical form.
                decorate(anchor, { path: direct, kind: null, access: null });
                if (scope) consider(anchor, direct); else anchor.dataset.pathState = 'yes';
                continue;
            }
            const relative = relativeTargetOf(href);
            if (!relative || !scope) {
                anchor.dataset.pathState = 'no';
                continue;
            }
            consider(anchor, relative);
        }
        if (!scope) return;
        // Bare paths already wrapped by an earlier pass are checked again after a reset.
        for (const span of block.querySelectorAll('span.file-link[data-path-input]:not([data-path-state])'))
            consider(span, span.dataset.pathInput);
        for (const code of block.querySelectorAll('code:not([data-path-state])')) {
            if (code.closest('pre, a')) {
                code.dataset.pathState = 'no';
                continue;
            }
            const text = code.textContent.trim();
            if (looksLikePath(text)) consider(code, text);
            else code.dataset.pathState = 'no';
        }
        scanText(block);
    };

    // Bare Windows paths in prose. A text node is split only around paths the Host confirmed.
    const scanText = block => {
        const walker = document.createTreeWalker(block, NodeFilter.SHOW_TEXT, {
            acceptNode: node => node.data.indexOf(':\\') < 1 || node.parentElement?.closest(NOT_PROSE)
                ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT
        });
        const nodes = [];
        while (walker.nextNode()) nodes.push(walker.currentNode);
        for (const node of nodes) {
            const found = windowsPathsIn(node.data);
            if (found.length === 0) continue;
            let unknown = false;
            for (const item of found) {
                if (!cache.has(item.path)) {
                    unknown = true;
                    queue(item.path);
                }
            }
            if (unknown) {
                block.dataset.pathTextPending = 'true';
                continue;
            }
            const fragment = document.createDocumentFragment();
            let offset = 0;
            for (const item of found) {
                const resolved = cache.get(item.path);
                if (!resolved) continue;
                fragment.append(node.data.slice(offset, item.index));
                const span = document.createElement('span');
                span.textContent = item.path;
                span.dataset.pathInput = item.path;
                span.dataset.pathState = 'yes';
                decorate(span, resolved);
                fragment.append(span);
                offset = item.index + item.path.length;
            }
            if (offset === 0) continue;
            fragment.append(node.data.slice(offset));
            node.replaceWith(fragment);
        }
    };

    const runScan = () => {
        scanTimer = 0;
        if (disposed) return;
        const started = performance.now();
        for (const block of dirty) {
            dirty.delete(block);
            scanBlock(block);
            if (dirty.size > 0 && performance.now() - started > SLICE_MS) {
                scheduleScan(0);
                return;
            }
        }
    };

    const scheduleScan = delay => {
        if (disposed || scanTimer) return;
        scanTimer = setTimeout(runScan, delay);
    };

    const markDirty = node => {
        const element = node instanceof Element ? node : node.parentElement;
        if (!element || !container.contains(element)) return;
        const block = element.closest(BLOCK);
        if (block) dirty.add(block);
        else for (const inner of element.querySelectorAll(BLOCK)) dirty.add(inner);
    };

    const queue = input => {
        if (!scope || inFlight.has(input)) return;
        pending.add(input);
        if (!flushTimer) flushTimer = setTimeout(flush, FLUSH_DELAY_MS);
    };

    const flush = async () => {
        flushTimer = 0;
        if (disposed || !scope || !resolveUrl || pending.size === 0) return;
        // While a scan is still going, wait for it unless a full batch is ready: fewer, fuller requests.
        if (dirty.size > 0 && pending.size < BATCH_SIZE) {
            flushTimer = setTimeout(flush, FLUSH_DELAY_MS);
            return;
        }
        const batch = [...pending].slice(0, BATCH_SIZE);
        for (const input of batch) {
            pending.delete(input);
            inFlight.add(input);
        }
        const requested = scope;
        let results = [];
        try {
            const response = await fetch(resolveUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ paths: batch })
            });
            if (response.ok) results = await response.json();
        } catch {
            results = [];
        }
        for (const input of batch) inFlight.delete(input);
        if (disposed || requested !== scope) return;
        // An empty answer is a failure, not "none of these exist": leave them unknown rather than
        // caching a wrong verdict for the rest of the session.
        if (results.length > 0) {
            const byInput = new Map(results.map(item => [item.input, item]));
            for (const input of batch) {
                const item = byInput.get(input);
                cache.set(input, item?.path
                    ? { path: item.path, kind: kindName(item.kind), access: accessName(item.access) }
                    : null);
            }
            applyResolved();
        }
        if (pending.size > 0 && !flushTimer) flushTimer = setTimeout(flush, FLUSH_DELAY_MS);
    };

    const applyResolved = () => {
        for (const element of container.querySelectorAll('[data-path-state="pending"]')) {
            const input = element.dataset.pathInput;
            if (input && cache.has(input)) consider(element, input);
        }
        for (const block of container.querySelectorAll(`${BLOCK}[data-path-text-pending]`)) {
            delete block.dataset.pathTextPending;
            dirty.add(block);
        }
        scheduleScan(0);
    };

    const onClick = event => {
        const anchor = event.target instanceof Element ? event.target.closest('a[href]') : null;
        if (!anchor || !container.contains(anchor)) return;
        const href = anchor.getAttribute('href') ?? '';
        // An "@" link to a chat or a review opens it; the page knows the reference by its id.
        if (href.startsWith('#mention-')) {
            event.preventDefault();
            dotnet.invokeMethodAsync('OnMentionLinkClicked', href.slice('#mention-'.length));
            return;
        }
        // A local path, or any relative link, would navigate the app itself; neither may.
        if (anchor.dataset.filePath || directPathOf(href) || relativeTargetOf(href)) event.preventDefault();
    };

    const onContextMenu = event => {
        const target = event.target instanceof Element ? event.target.closest('[data-file-path]') : null;
        if (!target || !container.contains(target)) return;
        event.preventDefault();
        dotnet.invokeMethodAsync('OnFileLinkContextMenu', target.dataset.filePath, event.clientX, event.clientY,
            target.dataset.fileKind ?? null, target.dataset.fileAccess === 'none' ? false : null);
    };

    const observer = new MutationObserver(records => {
        for (const record of records) {
            markDirty(record.target);
            for (const node of record.addedNodes) markDirty(node);
        }
        if (dirty.size > 0) scheduleScan(SCAN_DELAY_MS);
    });

    container.addEventListener('click', onClick);
    container.addEventListener('contextmenu', onContextMenu);
    observer.observe(container, { childList: true, subtree: true, characterData: true });
    for (const block of container.querySelectorAll(BLOCK)) dirty.add(block);
    scheduleScan(0);

    // Every verdict is dropped and the transcript is looked at again.
    const reset = () => {
        cache.clear();
        pending.clear();
        // The input stays, so every element is asked about the same path again.
        for (const element of container.querySelectorAll('[data-path-state]')) delete element.dataset.pathState;
        for (const block of container.querySelectorAll(BLOCK)) dirty.add(block);
        scheduleScan(0);
    };

    return {
        // A different project reads different directories.
        setScope: (next, url) => {
            scope = next || null;
            resolveUrl = url || null;
            reset();
        },
        // The project's directories changed, so what it may read did too.
        refresh: reset,
        dispose: () => {
            disposed = true;
            observer.disconnect();
            clearTimeout(scanTimer);
            clearTimeout(flushTimer);
            container.removeEventListener('click', onClick);
            container.removeEventListener('contextmenu', onContextMenu);
        }
    };
}
