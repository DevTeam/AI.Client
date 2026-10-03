import { readFileSync } from 'node:fs';
import { createContext, runInContext } from 'node:vm';
import { test } from 'node:test';
import assert from 'node:assert/strict';

const source = readFileSync(new URL('../../../src/AI.Web/wwwroot/js/appNavigationLinks.js', import.meta.url), 'utf8')
    .replace(/export /g, '');

function fixture() {
    const listeners = new Map(), calls = [], opened = [];
    class Element {
        constructor(href) { this.href = href; }
        closest() { return this; }
        getAttribute() { return this.href; }
    }
    const container = {
        contains: () => true,
        addEventListener(type, handler, capture) { listeners.set(type, { handler, capture }); },
        removeEventListener(type, handler, capture) {
            assert.equal(listeners.get(type).handler, handler);
            assert.equal(capture, true);
            listeners.delete(type);
        }
    };
    const context = createContext({ Element, URL, location: { href: 'https://app.test/' },
        window: { open: (...args) => opened.push(args) } });
    runInContext(source, context);
    const handle = context.attach(container, { invokeMethodAsync: (...args) => calls.push(args) });
    const click = (href, options = {}) => {
        const event = { type: 'click', button: 0, target: new Element(href), defaultPrevented: false, stopped: false,
            preventDefault() { this.defaultPrevented = true; }, stopPropagation() { this.stopped = true; }, ...options };
        listeners.get(event.type).handler(event);
        return event;
    };
    return { click, calls, opened, handle, listeners };
}

test('LLM shortcuts in guide text navigate without triggering question or step controls', () => {
    const f = fixture(), href = 'aiclient://navigate/settings.connections';
    for (const options of [{}, { ctrlKey: true }, { metaKey: true }, { type: 'auxclick', button: 1 }]) {
        const event = f.click(href, options);
        assert.equal(event.defaultPrevented, true);
        assert.equal(event.stopped, true);
    }
    assert.deepEqual(f.calls, Array.from({ length: 4 }, () => ['OnAppNavigationLinkClicked', href]));
    assert.equal(f.opened.length, 0);
});

test('transcript-handled links are not dispatched twice', () => {
    const f = fixture();
    f.click('aiclient://navigate/settings', { defaultPrevented: true });
    assert.equal(f.calls.length, 0);
});

test('unsafe or unknown schemes never leave the guide', () => {
    const f = fixture();
    for (const href of ['javascript:alert(1)', 'data:text/html,test', 'file:///C:/test', 'custom://host', 'relative/path'])
        assert.equal(f.click(href).defaultPrevented, true);
    assert.equal(f.calls.length, 0);
    assert.equal(f.opened.length, 0);
});

test('regular web links open separately and disposal removes listeners', () => {
    const f = fixture();
    f.click('https://example.com/');
    assert.deepEqual(f.opened, [['https://example.com/', '_blank', 'noopener,noreferrer']]);
    f.handle.dispose();
    assert.equal(f.listeners.size, 0);
});
