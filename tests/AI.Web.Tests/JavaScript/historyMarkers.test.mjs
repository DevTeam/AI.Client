import { readFileSync } from "node:fs";
import { createContext, runInContext } from "node:vm";
import { test } from "node:test";
import assert from "node:assert/strict";

const source = readFileSync(new URL("../../../src/AI.Web/wwwroot/js/historyMarkers.js", import.meta.url), "utf8")
    .replace(/export /g, "");

// A fake DOM just large enough for attach(): a strip as tall as the feed, three markers in a
// ribbon in its middle (11px apart, 8px tall, first one at y=300) and the tip/hint siblings.
function fixture() {
    const ticks = [];
    const element = (props = {}) => {
        const classes = new Set();
        const styleProps = {};
        const listeners = new Map();
        const item = {
            classes, styleProps, listeners,
            offsetTop: 0, offsetHeight: 8, offsetParent: null,
            scrollTop: 0, scrollHeight: 800, clientHeight: 700, clientWidth: 37,
            dataset: {},
            style: { setProperty(name, value) { styleProps[name] = value; } },
            classList: {
                add: name => classes.add(name),
                remove: name => classes.delete(name),
                contains: name => classes.has(name),
                toggle(name, force) {
                    if (force === undefined) return classes.has(name) ? (classes.delete(name), false) : (classes.add(name), true);
                    if (force) classes.add(name); else classes.delete(name);
                    return force;
                }
            },
            addEventListener: (type, handler) => {
                if (!listeners.has(type)) listeners.set(type, []);
                listeners.get(type).push(handler);
            },
            removeEventListener: (type, handler) => {
                const list = listeners.get(type) ?? [];
                const index = list.indexOf(handler);
                if (index >= 0) list.splice(index, 1);
            },
            getBoundingClientRect: () => ({ left: 0, right: 37, top: 0, bottom: 700, width: 37, height: 700 }),
            querySelector: () => null,
            querySelectorAll: () => [],
            appendChild() {},
            replaceChildren() {},
            ...props
        };
        return item;
    };

    const strip = element({ querySelectorAll: selector => selector === ".history-marker" ? markers : [] });
    const markers = [300, 311, 322].map((top, index) => element({
        offsetTop: top,
        dataset: { target: `message-${index}`, questionText: `Question ${index}` },
        getBoundingClientRect: () => ({ left: 0, right: 37, top, bottom: top + 8, width: 37, height: 8 })
    }));
    const tip = element({ offsetHeight: 40 });
    tip.offsetParent = element();
    const hintUp = element();
    const hintDown = element();
    strip.parentElement = {
        querySelector: selector => selector === ":scope > .history-marker-tip" ? tip
            : selector === ":scope > .history-scroll-hint-up" ? hintUp
                : selector === ":scope > .history-scroll-hint-down" ? hintDown : null
    };
    const scroller = element({ scrollHeight: 5000, clientHeight: 700 });

    const document = {
        getElementById: id => element({ offsetTop: 100 * Number(id.split("-")[1] ?? 0) }),
        createElement: () => element()
    };
    const context = createContext({
        document, window: { aiClientPlayRatchetTick: () => ticks.push(1) },
        localStorage: { getItem: () => null, setItem() {} },
        setTimeout, clearTimeout, setInterval, clearInterval,
        performance: { now: () => 0 },
        requestAnimationFrame: callback => setTimeout(callback, 0), cancelAnimationFrame: clearTimeout
    });
    runInContext(source, context);
    const stripHandle = context.attach(strip, scroller, null);
    const moveTo = clientY => strip.listeners.get("mousemove").forEach(handler => handler({ clientY }));
    const leave = () => strip.listeners.get("mouseleave").forEach(handler => handler());
    return { ticks, strip, markers, tip, moveTo, leave, dispose: () => stripHandle.dispose() };
}

test("the empty part of the strip above and below the dashes is not a hover", () => {
    const f = fixture();
    // The ribbon of dashes runs from y=300 to y=330; the strip itself is 700px tall.
    f.moveTo(40);
    assert.deepEqual(f.ticks, [], "passing the cursor 260px above the dashes must stay silent");
    assert.equal(f.tip.classes.has("visible"), false, "no preview far above the dashes");
    f.moveTo(650);
    assert.deepEqual(f.ticks, [], "passing the cursor 320px below the dashes must stay silent");
    assert.equal(f.tip.classes.has("visible"), false, "no preview far below the dashes");
    f.moveTo(334);
    assert.deepEqual(f.ticks, [], "4px past the last dash is already outside the ribbon");
    f.dispose();
});

test("a pointer on a dash still ticks once per crossing and shows its preview", () => {
    const f = fixture();
    f.moveTo(40);
    f.moveTo(300);
    assert.equal(f.ticks.length, 1, "entering the ribbon plays one tick");
    assert.equal(f.tip.classes.has("visible"), true);
    f.moveTo(311);
    assert.equal(f.ticks.length, 2, "crossing into the next dash plays the next tick");
    f.moveTo(650);
    f.moveTo(311);
    assert.equal(f.ticks.length, 3, "coming back from below the ribbon ticks again");
    f.leave();
    assert.equal(f.tip.classes.has("visible"), false);
    f.dispose();
});
