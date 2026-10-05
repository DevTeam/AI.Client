import { readFileSync } from 'node:fs';
import { createContext, runInContext } from 'node:vm';
import { test } from 'node:test';
import assert from 'node:assert/strict';

const source = readFileSync(new URL('../../../src/AI.Web/wwwroot/js/fileLinks.js', import.meta.url), 'utf8')
    .replace(/export /g, '');

function fixture(desktop = false, hrefs = []) {
    const listeners = new Map(), opened = [], messages = [], mentions = [], scrolled = [];
    const timers = [];
    class Element {
        constructor(href) { this.href = href; this.dataset = {}; }
        closest() { return this; }
        getAttribute() { return this.href; }
    }
    const anchors = hrefs.map(href => new Element(href));
    const block = { isConnected: true, querySelectorAll: () => anchors.filter(anchor => !anchor.dataset.pathState) };
    const container = {
        contains: () => true,
        querySelectorAll: () => hrefs.length ? [block] : [],
        addEventListener(type, handler, capture) { listeners.set(type, { handler, capture }); },
        removeEventListener(type, handler, capture) {
            assert.equal(listeners.get(type).handler, handler);
            assert.equal(listeners.get(type).capture, capture);
            listeners.delete(type);
        }
    };
    const context = createContext({
        Element, URL, location: { href: 'https://app.test/?chat=current' },
        window: { open: (...args) => opened.push(args) },
        document: { getElementById: id => ({ scrollIntoView: () => scrolled.push(id) }) },
        MutationObserver: class { observe() {} disconnect() {} },
        performance: { now: () => 0 },
        setTimeout: callback => { timers.push(callback); return timers.length; }, clearTimeout() {}
    });
    if (desktop) context.invokeCSharpAction = json => messages.push(JSON.parse(json));
    runInContext(source, context);
    const handle = context.attach(container, { invokeMethodAsync: (...args) => mentions.push(args) });
    function click(href, options = {}) {
        const event = { type: 'click', button: 0, target: new Element(href), defaultPrevented: false,
            preventDefault() { this.defaultPrevented = true; }, ...options };
        listeners.get(event.type).handler(event);
        return event;
    }
    return { click, handle, listeners, opened, messages, mentions, scrolled, anchors,
        scan: () => timers.shift()?.() };
}

test('rendered web links have a safe new-tab target for native browser actions', () => {
    const f = fixture(false, ['https://example.com', '//example.com', 'mailto:hello@example.com']);
    f.scan();
    for (const anchor of f.anchors) {
        assert.equal(anchor.target, '_blank');
        assert.equal(anchor.rel, 'noopener noreferrer');
    }
});

test('application shortcuts go to Blazor without leaving the transcript', () => {
    for (const desktop of [false, true]) {
        const f = fixture(desktop);
        const href = 'aiclient://navigate/settings.connections';
        for (const options of [{}, { ctrlKey: true }, { type: 'auxclick', button: 1 }])
            assert.equal(f.click(href, options).defaultPrevented, true);
        assert.deepEqual(f.mentions, Array.from({ length: 3 }, () => ['OnAppNavigationLinkClicked', href]));
        assert.equal(f.opened.length, 0);
        assert.equal(f.messages.length, 0);
    }
});

test('desktop links use the bridge before any navigation, including modifier and middle clicks', () => {
    const f = fixture(true);
    for (const options of [{}, { ctrlKey: true }, { metaKey: true }, { shiftKey: true }, { type: 'auxclick', button: 1 }]) {
        assert.equal(f.click('https://example.com/path?q=1#part', options).defaultPrevented, true);
    }
    assert.equal(f.messages.length, 5);
    assert.deepEqual(f.messages[0], { type: 'open-external-link', url: 'https://example.com/path?q=1#part' });
    assert.equal(f.opened.length, 0);
    assert.equal(f.listeners.get('click').capture, true);
    assert.equal(f.listeners.get('auxclick').capture, true);
});

test('browser links open a separate tab without access to the opener', () => {
    const f = fixture();
    for (const href of ['https://example.com', 'http://example.com', '//example.com/path',
        'https://app.test/another-page', 'mailto:hello@example.com']) {
        assert.equal(f.click(href).defaultPrevented, true);
    }
    assert.deepEqual(f.opened, [
        ['https://example.com/', '_blank', 'noopener,noreferrer'],
        ['http://example.com/', '_blank', 'noopener,noreferrer'],
        ['https://example.com/path', '_blank', 'noopener,noreferrer'],
        ['https://app.test/another-page', '_blank', 'noopener,noreferrer'],
        ['mailto:hello@example.com', '_blank', 'noopener,noreferrer']
    ]);
});

test('local paths, malformed URLs and unknown schemes never navigate or launch another app', () => {
    const f = fixture(true);
    for (const href of ['file:///C:/repo/app.cs', 'C:\\repo\\app.cs', 'src/app.cs', '/repo/app.cs',
        '../docs', '', 'javascript:alert(1)', 'data:text/html,hello', 'custom:command', 'https://[invalid']) {
        assert.equal(f.click(href).defaultPrevented, true, href);
    }
    assert.equal(f.messages.length, 0);
    assert.equal(f.opened.length, 0);
    assert.deepEqual(f.mentions, [
        ['OnFileLinkClicked', 'C:\\repo\\app.cs', null, null], ['OnFileLinkClicked', 'C:\\repo\\app.cs', null, null],
        ['OnFileLinkClicked', 'src/app.cs', null, null], ['OnFileLinkClicked', '/repo/app.cs', null, null],
        ['OnFileLinkClicked', '../docs', null, null]
    ]);
});

test('decorated file spans open the viewer by click and keyboard', () => {
    const f = fixture();
    const initial = f.click('ignored');
    f.mentions.length = 0;
    const target = initial.target;
    target.href = null;
    target.dataset.filePath = 'D:\\Sandbox\\picture.png';
    target.tagName = 'SPAN';
    f.click(null, { target });
    f.click(null, { target, type: 'keydown', key: 'Enter' });
    assert.deepEqual(f.mentions, Array.from({ length: 2 }, () => ['OnFileLinkClicked', 'D:\\Sandbox\\picture.png', null, null]));
});

test('source links keep lines separate from file paths before and after canonical resolution', () => {
    const f = fixture();
    for (const href of ['file:///C:/repo/app.cs#L12-L18', 'C:\\repo\\app.cs:12-18', 'src/app.cs#L12-18',
        'src/app.cs:12-18', 'file://server/share/app.cs#L12-L18', 'file:///home/me/app.cs#L12-L18']) {
        const event = f.click(href);
        const expected = href.startsWith('file://server') ? '\\\\server\\share\\app.cs'
            : href.startsWith('file:///home') ? '/home/me/app.cs'
            : href.startsWith('src') ? 'src/app.cs' : 'C:\\repo\\app.cs';
        assert.deepEqual(f.mentions.at(-1), ['OnFileLinkClicked', expected, 12, 18]);
        event.target.dataset.filePath = 'C:\\canonical\\app.cs';
        event.target.dataset.pathInput = expected;
        f.click(href, { target: event.target });
        assert.deepEqual(f.mentions.at(-1), ['OnFileLinkClicked', 'C:\\canonical\\app.cs', 12, 18]);
    }
    for (const href of ['src/app.cs#L12', 'src/app.cs:12', 'src/app.cs:12:7']) {
        f.click(href);
        assert.deepEqual(f.mentions.at(-1), ['OnFileLinkClicked', 'src/app.cs', 12, 12]);
    }
    for (const suffix of ['#L0', '#L18-L12', ':18-12', '#L2147483648', '#L1-L99999999999999999999']) {
        f.click('src/app.cs' + suffix);
        assert.deepEqual(f.mentions.at(-1), ['OnFileLinkClicked', 'src/app.cs', null, null]);
    }
    f.click('file:///C:/repo/My%20File%23L12.txt#L3');
    assert.deepEqual(f.mentions.at(-1), ['OnFileLinkClicked', 'C:\\repo\\My File#L12.txt', 3, 3]);
    f.click('file:///C:/repo/File%23L12');
    assert.deepEqual(f.mentions.at(-1), ['OnFileLinkClicked', 'C:\\repo\\File#L12', null, null]);
    const target = f.click('ignored').target;
    target.href = null;
    target.dataset.filePath = 'C:\\canonical\\app.cs';
    target.dataset.pathInput = 'src/app.cs:12-18';
    target.tagName = 'CODE';
    f.click(null, { target, type: 'keydown', key: 'Enter' });
    assert.deepEqual(f.mentions.at(-1), ['OnFileLinkClicked', 'C:\\canonical\\app.cs', 12, 18]);
    f.click('https://example.com/app.cs#L12-L18');
    assert.equal(f.opened.at(-1)[0], 'https://example.com/app.cs#L12-L18');
});

test('mentions and fragment scrolling work without changing the current page', () => {
    const f = fixture(true);
    assert.equal(f.click('#mention-chat-id').defaultPrevented, true);
    assert.deepEqual(f.mentions, [['OnMentionLinkClicked', 'chat-id']]);
    assert.equal(f.click('#chapter%202').defaultPrevented, true);
    assert.deepEqual(f.scrolled, ['chapter 2']);
    assert.doesNotThrow(() => f.click('#%invalid'));
    assert.equal(f.messages.length, 0);
});

test('right clicks and previously handled events are left alone; disposal removes listeners', () => {
    const f = fixture(true);
    assert.equal(f.click('https://example.com', { type: 'auxclick', button: 2 }).defaultPrevented, false);
    f.click('https://example.com', { defaultPrevented: true });
    assert.equal(f.messages.length, 0);
    f.handle.dispose();
    assert.equal(f.listeners.size, 0);
});

// A small DOM for the observer/cache path. Timers and mutation delivery are explicit so the
// assertions see what is ready for the first paint, before the delayed discovery scan runs.
function scanningFixture() {
    const blocks = [], timers = new Map(), requests = [];
    let timerId = 0, clock = 0, deliver;
    class Element {
        constructor(tagName) {
            this.tagName = tagName;
            this.dataset = {};
            this.attributes = new Map();
            const classes = new Set();
            this.classList = { add: value => classes.add(value), remove: value => classes.delete(value),
                contains: value => classes.has(value) };
            this.isConnected = true;
        }
        setAttribute(name, value) { this.attributes.set(name, value); }
        getAttribute(name) { return this.attributes.get(name) ?? null; }
        removeAttribute(name) { this.attributes.delete(name); }
        closest(selector) { return selector === '.markdown-content, .message-resource-list' ? this.block : null; }
        querySelectorAll(selector) {
            this.scans++;
            const elements = selector.startsWith('code') ? this.codes : selector.startsWith('span') ? this.spans : [];
            return elements.filter(element => !element.dataset.pathState);
        }
    }
    function block(codeText = null, prose = null) {
        const result = new Element('DIV');
        result.block = result;
        result.codes = [];
        result.spans = [];
        result.texts = [];
        result.scans = 0;
        if (codeText) {
            const code = new Element('CODE');
            code.textContent = codeText;
            code.block = result;
            result.codes.push(code);
        }
        if (prose) {
            const textNode = data => ({ data, parentElement: result, replaceWith(fragment) {
                const index = result.texts.indexOf(this);
                result.texts.splice(index, 1, ...fragment.parts.filter(part => typeof part === 'string').map(textNode));
                result.spans.push(...fragment.parts.filter(part => part instanceof Element));
            } });
            result.texts.push(textNode(prose));
        }
        blocks.push(result);
        return result;
    }
    const container = {
        contains: () => true,
        addEventListener() {}, removeEventListener() {},
        querySelectorAll(selector) {
            if (selector === '.markdown-content, .message-resource-list') return blocks;
            if (selector === '[data-path-state]') return blocks.flatMap(block => [...block.codes, ...block.spans])
                .filter(element => element.dataset.pathState);
            if (selector === '[data-path-state="pending"]') return blocks.flatMap(block => [...block.codes, ...block.spans])
                .filter(element => element.dataset.pathState === 'pending');
            if (selector.endsWith('[data-path-text-pending]')) return blocks.filter(block => block.dataset.pathTextPending);
            return [];
        }
    };
    const context = createContext({
        Element, URL, location: { href: 'https://app.test/' },
        NodeFilter: { SHOW_TEXT: 4, FILTER_ACCEPT: 1, FILTER_REJECT: 2 },
        document: {
            createElement: tag => new Element(tag.toUpperCase()),
            createDocumentFragment: () => ({ parts: [], append(part) { this.parts.push(part); } }),
            createTreeWalker(block, _, filter) {
                const nodes = block.texts.filter(node => filter.acceptNode(node) === 1);
                let index = 0;
                return { nextNode() { this.currentNode = nodes[index++]; return !!this.currentNode; } };
            }
        },
        MutationObserver: class {
            constructor(callback) { deliver = callback; }
            observe() {} disconnect() {} takeRecords() { return []; }
        },
        performance: { now: () => clock += 9 },
        setTimeout(callback) { timers.set(++timerId, callback); return timerId; },
        clearTimeout(id) { timers.delete(id); },
        async fetch(_, options) {
            const paths = JSON.parse(options.body).paths;
            requests.push(...paths);
            return { ok: true, json: async () => paths.map(input => ({ input, path: input, kind: 0, access: 1 })) };
        }
    });
    runInContext(source, context);
    const handle = context.attach(container, { invokeMethodAsync() {} });
    handle.setScope('project', '/resolve');
    return { block, requests,
        async tick() {
            const [id, callback] = timers.entries().next().value;
            timers.delete(id);
            await callback();
        },
        change(...targets) { deliver(targets.map(target => ({ target, addedNodes: [] }))); }
    };
}

test('all cached file links are restored before paint when several message blocks change', async () => {
    const f = scanningFixture();
    const original = f.block('src/app.cs');
    f.change(original);
    await f.tick(); // Discover the path.
    await f.tick(); // Resolve and cache it.
    assert.equal(original.codes[0].classList.contains('file-link'), true);
    original.scans = 0;
    const first = f.block('src/app.cs'), second = f.block('src/app.cs');
    f.change(first, second);
    for (const block of [first, second]) {
        assert.equal(block.codes[0].classList.contains('file-link'), true);
        assert.equal(block.codes[0].dataset.fileAccess, 'read');
    }
    assert.equal(original.scans, 0, 'unchanged blocks waiting for the timer must not be scanned');
    assert.deepEqual(f.requests, ['src/app.cs']);
});

test('a new unknown path in growing prose does not hide a cached file link before paint', async () => {
    const f = scanningFixture();
    const original = f.block(null, 'Read C:\\repo\\app.cs');
    f.change(original);
    await f.tick();
    await f.tick();
    await f.tick(); // Finish the first prose decoration.
    const grown = f.block(null, 'Read C:\\repo\\app.cs and C:\\repo\\next.cs');
    f.change(grown);
    assert.equal(grown.spans.length, 1);
    assert.equal(grown.spans[0].dataset.filePath, 'C:\\repo\\app.cs');
    assert.equal(grown.spans[0].classList.contains('file-link'), true);
    assert.deepEqual(f.requests, ['C:\\repo\\app.cs']);
    await f.tick(); // Discover only the new path.
    await f.tick(); // Resolve it.
    await f.tick(); // Decorate the remaining prose.
    assert.deepEqual(grown.spans.map(span => span.dataset.filePath), ['C:\\repo\\app.cs', 'C:\\repo\\next.cs']);
    assert.deepEqual(f.requests, ['C:\\repo\\app.cs', 'C:\\repo\\next.cs']);
});
