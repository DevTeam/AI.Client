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
        ['OnFileLinkClicked', 'C:\\repo\\app.cs'], ['OnFileLinkClicked', 'C:\\repo\\app.cs'],
        ['OnFileLinkClicked', 'src/app.cs'], ['OnFileLinkClicked', '/repo/app.cs'],
        ['OnFileLinkClicked', '../docs']
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
    assert.deepEqual(f.mentions, Array.from({ length: 2 }, () => ['OnFileLinkClicked', 'D:\\Sandbox\\picture.png']));
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
