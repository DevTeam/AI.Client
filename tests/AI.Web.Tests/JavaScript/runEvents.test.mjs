import { readFileSync } from 'node:fs';
import { createContext, runInContext } from 'node:vm';
import { test } from 'node:test';
import assert from 'node:assert/strict';

const source = readFileSync(new URL('../../../src/AI.Web/wwwroot/js/runEvents.js', import.meta.url), 'utf8')
    .replace(/export /g, '');

class Element {
    constructor(insidePicker) { this.insidePicker = insidePicker; }
    closest(selector) { return selector === '.branch-picker-trigger, .branch-picker-menu' && this.insidePicker ? this : null; }
}

function setUp(menuOpen) {
    const listeners = [];
    const calls = [];
    const document = {
        querySelector: selector => selector === '.branch-picker-menu' && menuOpen ? {} : null,
        addEventListener: (type, handler) => listeners.push({ type, handler }),
        removeEventListener: (type, handler) => listeners.splice(listeners.findIndex(item => item.handler === handler), 1)
    };
    const context = createContext({ document, Element });
    runInContext(source, context);
    const subscription = context.watchBranchPickerDismiss({ invokeMethodAsync: name => calls.push(name) });
    const press = (target, button = 0) => listeners.forEach(item => item.handler({ button, target }));
    return { calls, listeners, press, subscription };
}

test('a press outside the open branch picker closes it', () => {
    const { calls, press } = setUp(true);
    press(new Element(false));
    assert.deepEqual(calls, ['OnBranchPickerDismissed']);
});

test('a press on the picker itself, another button or a closed picker leaves it alone', () => {
    const open = setUp(true);
    open.press(new Element(true));
    open.press(new Element(false), 2);
    assert.deepEqual(open.calls, []);
    const closed = setUp(false);
    closed.press(new Element(false));
    assert.deepEqual(closed.calls, []);
});

test('disposing stops watching', () => {
    const { listeners, subscription } = setUp(true);
    subscription.dispose();
    assert.equal(listeners.length, 0);
});
