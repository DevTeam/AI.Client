import { readFileSync } from 'node:fs';
import { createContext, runInContext } from 'node:vm';
import { test } from 'node:test';
import assert from 'node:assert/strict';

const source = readFileSync(new URL('../../../src/AI.Web/wwwroot/js/workspaceNavigation.js', import.meta.url), 'utf8')
    .replace(/export /g, '');
const pickerSource = readFileSync(new URL('../../../src/AI.Web/wwwroot/js/directoryPicker.js', import.meta.url), 'utf8')
    .replace(/export /g, '');

function fixture() {
    const listeners = new Map(), calls = [], remembered = [], moves = [], pending = [];
    const location = { href: 'https://app.test/' };
    const entries = [{ state: null, url: location.href }];
    let index = 0, dialogs = [];
    const history = {
        get state() { return entries[index].state; },
        replaceState(state, _, url) { entries[index] = { state, url: String(url) }; location.href = String(url); },
        pushState(state, _, url) {
            entries.splice(++index);
            entries.push({ state, url: String(url) });
            location.href = String(url);
        },
        go(delta) { moves.push(delta); pending.push(delta); },
        back() { this.go(-1); }, forward() { this.go(1); }
    };
    const window = {
        addEventListener(type, handler, capture) { listeners.set(type, { handler, capture }); },
        removeEventListener(type, handler, capture) {
            assert.equal(listeners.get(type).handler, handler);
            assert.equal(listeners.get(type).capture, capture);
            listeners.delete(type);
        }
    };
    const storage = new Map();
    const context = createContext({ history, location, window, URL,
        document: { querySelectorAll: () => dialogs },
        sessionStorage: { getItem: key => storage.get(key), setItem: (key, value) => storage.set(key, value) },
        CustomEvent: class { constructor(type, options) { this.type = type; Object.assign(this, options); } },
        invokeCSharpAction: json => remembered.push(JSON.parse(json)) });
    runInContext(source, context);
    const handle = context.attach({ invokeMethodAsync: (...args) => calls.push(args) });
    const mouse = (type, button) => {
        const event = { type, button, prevented: false, stopped: false,
            preventDefault() { this.prevented = true; }, stopPropagation() { this.stopped = true; } };
        listeners.get(type).handler(event);
        return event;
    };
    const pop = delta => {
        index += delta;
        location.href = entries[index].url;
        listeners.get('popstate').handler();
    };
    return { handle, mouse, pop, context, calls, remembered, moves, pending, listeners, location,
        setDialogs: value => { dialogs = value; },
        flush() { while (pending.length) pop(pending.shift()); } };
}

test('side mouse buttons reach only the active modal and fire once per press', () => {
    const f = fixture(), outer = [], inner = [];
    f.setDialogs([{ dispatchEvent: event => outer.push(event.detail) }, { dispatchEvent: event => inner.push(event.detail) }]);
    for (const button of [3, 4]) {
        for (const type of ['mousedown', 'mouseup', 'auxclick']) {
            const event = f.mouse(type, button);
            assert.equal(event.prevented, true);
            assert.equal(event.stopped, true);
        }
    }
    assert.deepEqual(inner, ['Back', 'Forward']);
    assert.deepEqual(outer, []);
    assert.equal(f.calls.length, 0);
    assert.equal(f.mouse('mousedown', 0).prevented, false);
    assert.equal(f.mouse('auxclick', 1).prevented, false);
});

test('the directory picker receives local history commands and removes its listener on disposal', () => {
    const f = fixture(), listeners = new Map(), calls = [];
    const dialog = {
        addEventListener: (type, handler) => listeners.set(type, handler),
        removeEventListener: (type, handler) => { assert.equal(listeners.get(type), handler); listeners.delete(type); },
        dispatchEvent: event => listeners.get(event.type)?.(event)
    };
    f.context.document.addEventListener = () => {};
    f.context.document.removeEventListener = () => {};
    runInContext(pickerSource, f.context);
    const keys = f.context.attach(dialog, { invokeMethodAsync: (...args) => calls.push(args) });
    f.setDialogs([dialog]);
    f.mouse('mousedown', 3);
    f.mouse('mousedown', 4);
    assert.deepEqual(calls, [['OnPickerKey', 'Back', '', null], ['OnPickerKey', 'Forward', '', null]]);
    keys.dispose();
    assert.equal(listeners.size, 0);
});

test('modal blocks workspace API traversal and restores browser traversal without notifying the workspace', () => {
    const f = fixture();
    f.handle.set('project', 'first');
    f.handle.set('project', 'second');
    f.handle.back();
    f.flush();
    f.calls.length = 0;
    f.remembered.length = 0;
    f.moves.length = 0;
    const url = f.location.href;
    f.setDialogs([{ dispatchEvent() {} }]);
    f.handle.back();
    f.handle.forward();
    assert.deepEqual(f.moves, []);
    f.pop(-1);
    assert.deepEqual(f.moves, [1]);
    // Closing the modal before restoration finishes must not apply the attempted navigation.
    f.setDialogs([]);
    f.flush();
    assert.equal(f.location.href, url);
    assert.equal(f.calls.length, 0);
    assert.equal(f.remembered.length, 0);
    f.setDialogs([{ dispatchEvent() {} }]);
    f.pop(1);
    f.flush();
    assert.equal(f.location.href, url);
    assert.equal(f.calls.length, 0);
    f.setDialogs([]);
    f.handle.forward();
    f.flush();
    assert.equal(f.calls.length, 1);
    assert.match(f.calls[0][1], /chat=second/);
});

test('without a modal mouse buttons and workspace history retain normal behavior; disposal removes listeners', () => {
    const f = fixture();
    assert.equal(f.mouse('mousedown', 3).prevented, false);
    assert.equal(f.mouse('mouseup', 4).prevented, false);
    f.handle.set('project', 'chat');
    f.handle.back();
    f.flush();
    assert.equal(f.calls.length, 1);
    assert.equal(f.calls[0][0], 'OnWorkspaceHistoryChanged');
    f.handle.dispose();
    assert.equal(f.listeners.size, 0);
});
