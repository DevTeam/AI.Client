import { readFileSync } from "node:fs";
import { createContext, runInContext } from "node:vm";
import { test } from "node:test";
import assert from "node:assert/strict";

const source = readFileSync(new URL("../../../src/AI.Web/wwwroot/js/appGuide.js", import.meta.url), "utf8")
    .replace(/import .*?;\r?\n/, "const cancelCursor = () => {};\n").replace(/export /g, "");
const settle = () => new Promise(resolve => setTimeout(resolve, 40));

test("a guided field is revealed through closed disclosures without activating or changing it", async () => {
    const f = fixture();
    const outer = { tagName: "DETAILS", open: false, parentElement: null };
    const inner = { tagName: "DETAILS", open: false, parentElement: outer };
    f.target.parentElement = inner;
    f.target.getClientRects = () => outer.open && inner.open ? [1] : [];
    const request = { target: "settings.connection.context_window", action: "show" };
    assert.equal(f.context.available(request), false);
    assert.equal(outer.open, false, "discovery must not change visibility");
    assert.equal(await f.context.waitForTarget(request), true);
    assert.equal(inner.open, true);
    assert.equal(outer.open, true);
    assert.equal(f.target.clicks, undefined);
});

function fixture(reduced = false) {
    const created = [], animations = [];
    function element(control = false) {
        const attributes = new Map();
        const item = { style: { setProperty(name, value) { this[name] = value; } }, dataset: {}, classes: [], children: [],
            removed: false, isConnected: true,
            matches: () => control, querySelector: () => null, querySelectorAll: () => [],
            getClientRects: () => [1],
            getBoundingClientRect: () => ({ left: 20, top: 20, right: 100, bottom: 50, width: 80, height: 30 }),
            getAttribute: name => attributes.get(name) ?? null, setAttribute: (name, value) => attributes.set(name, value),
            hasAttribute: name => attributes.has(name), removeAttribute: name => attributes.delete(name),
            scrollIntoView() {}, dispatchEvent() {},
            remove() { this.removed = true; }, appendChild(child) { this.children.push(child); },
            cloneNode() { const copy = element(); copy.className = this.className; copy.classes = [...this.classes];
                for (const [name, value] of attributes) copy.setAttribute(name, value); created.push(copy); return copy; },
            click() { this.clicks = (this.clicks || 0) + 1; }, focus() {},
            animate(frames, options) {
                let reject;
                const finished = new Promise((resolve, fail) => { reject = fail; setTimeout(resolve, 5); });
                finished.catch(() => {});
                const animation = { element: item, frames, options, finished, cancel() { reject(new DOMException("Cancelled", "AbortError")); } };
                animations.push(animation);
                return animation;
            } };
        item.classList = { add: value => item.classes.push(value), remove() {} };
        return item;
    }
    const has = (item, cls) => (item.className || "").split(" ").includes(cls) || item.classes.includes(cls);
    // Understands the selectors the guide uses: ".a", ".a:not(.b)", ".a[attr]" and comma lists.
    const matches = (item, selector) => selector.split(",").some(part => {
        const [, cls, attribute, not] = part.trim().match(/^\.([\w-]+)(?:\[([\w-]+)\])?(?::not\(\.([\w-]+)\))?$/) || [];
        return cls && has(item, cls) && (!attribute || item.hasAttribute(attribute)) && (!not || !has(item, not));
    });
    const target = element(true), card = element();
    card.className = "app-guide-step";
    const document = {
        querySelector: selector => selector.startsWith("[data-app-target") ? target
            : [card, ...created].find(item => !item.removed && matches(item, selector)) || null,
        querySelectorAll: selector => created.filter(item => !item.removed && matches(item, selector)),
        createElement() { const item = element(); created.push(item); return item; },
        body: { appendChild() {} }
    };
    const context = createContext({ document, matchMedia: () => ({ matches: reduced }), CSS: { escape: value => value },
        innerWidth: 800, innerHeight: 600, AbortController, DOMException, Event, PointerEvent: Event, MouseEvent: Event,
        setTimeout: callback => setTimeout(callback, 5), clearTimeout });
    runInContext(source, context);
    return { context, target, card, created, animations,
        travel: () => animations.filter(item => item.frames[0].transform && item.element.className?.includes("app-guide-cursor")),
        cursor: () => created.filter(item => item.className?.includes("app-guide-cursor")).at(-1) };
}

test("show moves the pointer along a path to a control and keeps it visible after positioning the comment", async () => {
    const f = fixture(), request = { target: "chat.composer", action: "show" };
    await f.context.show(request);
    assert.equal(await f.context.perform(request), null);
    const [travel] = f.travel();
    assert.equal(f.travel().length, 1);
    assert.equal(travel.frames.length, 3);
    assert.notEqual(travel.frames[0].transform, travel.frames.at(-1).transform);
    assert.ok(travel.options.duration >= 500 && travel.options.duration <= 1100);
    f.context.position(request);
    assert.equal(f.cursor().removed, false);
    assert.equal(f.target.clicks, undefined);
    f.context.clear();
    await settle();
    assert.equal(f.cursor().removed, true);
});

test("show scrolls an off-screen tall widget within its pane and keeps the pointer in the visible portion", async () => {
    const f = fixture();
    let moved = false, documentScrolled = false;
    const pane = { scrollTop: 0,
        getBoundingClientRect: () => ({ left: 600, right: 800, top: 100, bottom: 500 }),
        scrollTo(options) { this.scrollTop = options.top; moved = true; } };
    f.target.closest = selector => selector === ".chat-widgets-list" ? pane : null;
    f.target.scrollIntoView = () => { documentScrolled = true; };
    f.target.getBoundingClientRect = () => ({ left: 600, right: 800, width: 200,
        top: moved ? 108 : 700, bottom: moved ? 908 : 1500, height: 800 });
    const request = { target: "widgets.chat-usage", action: "show" };
    assert.equal(await f.context.show(request), true);
    assert.equal(pane.scrollTop, 592);
    assert.equal(documentScrolled, false);
    assert.equal(await f.context.perform(request), null);
    const end = f.travel()[0].frames.at(-1).transform;
    const [, x, y] = end.match(/translate\(([\d.]+)px, ([\d.]+)px\)/);
    assert.ok(Number(x) >= 600 && Number(x) <= 800);
    assert.ok(Number(y) >= 100 && Number(y) <= 500);
    assert.equal(f.target.clicks, undefined);
});

test("Stop cancels travel before any deferred click", async () => {
    const f = fixture(), request = { target: "chat.context", action: "click" };
    await f.context.show(request);
    const pending = f.context.perform(request);
    f.context.cancelAnimation();
    assert.equal(await pending, "Action was stopped.");
    assert.equal(f.target.clicks, undefined);
    assert.equal(f.cursor().removed, true);
});

test("reduced motion still moves the pointer instead of making it appear over the control", async () => {
    const f = fixture(true), request = { target: "project.menu", action: "show" };
    await f.context.show(request);
    assert.equal(await f.context.perform(request), null);
    assert.equal(f.travel().length, 1);
    assert.equal(f.cursor().removed, false);
    assert.equal(f.target.clicks, undefined);
});

test("the next step carries the pointer on from where it is, without fading it out and back in", async () => {
    const f = fixture(), first = { target: "chat.composer", action: "show" };
    await f.context.show(first);
    await f.context.perform(first);
    const before = f.cursor();
    await f.context.show({ target: "project.menu", action: "click" });
    assert.ok(!before.classes.includes("is-leaving"));
    await f.context.perform({ target: "project.menu", action: "show" });
    assert.notEqual(f.cursor(), before);
    assert.equal(f.animations.filter(item => item.element === f.cursor() && item.frames[0].opacity === 0).length, 0);
});

test("a confirmed click has a press and ripple, and an expired click does not execute", async () => {
    const f = fixture(), request = { target: "project.menu", action: "click" };
    await f.context.show(request);
    assert.equal(await f.context.perform(request), null);
    assert.equal(f.target.clicks, 1);
    assert.ok(f.cursor().classes.includes("is-pressing"));
    assert.ok(f.cursor().children.some(child => child.className === "ghost-cursor-ripple"));
    assert.equal(await f.context.perform({ ...request, expiresAt: "2000-01-01T00:00:00Z" }), "Action expired.");
    assert.equal(f.target.clicks, 1);
});

test("the step card fades in once beside its control and points an arrow at it", async () => {
    const f = fixture(), request = { target: "chat.composer", action: "show" };
    await f.context.show(request);
    f.context.position(request);
    f.context.position(request);
    assert.ok(f.card.hasAttribute("data-placed"));
    assert.equal(f.card.dataset.side, "right");
    assert.equal(f.card.style.left, "114px");
    assert.equal(f.animations.filter(item => item.element === f.card).length, 1);
    assert.equal(f.animations.find(item => item.element === f.card).frames[0].opacity, 0);
});

test("dismiss fades out a detached copy of a placed card and the pointer", async () => {
    const f = fixture(), request = { target: "chat.composer", action: "show" };
    await f.context.show(request);
    await f.context.perform(request);
    f.context.position(request);
    f.context.dismiss();
    const ghost = f.created.find(item => item.className === "app-guide-step");
    assert.ok(ghost);
    assert.ok(ghost.classes.includes("is-leaving"));
    assert.equal(ghost.hasAttribute("data-placed"), false);
    assert.equal(ghost.inert, true);
    await settle();
    assert.equal(ghost.removed, true);
    assert.equal(f.cursor().removed, true);
});

test("dismiss leaves nothing behind when the card was never placed", async () => {
    const f = fixture();
    f.context.dismiss();
    assert.equal(f.created.length, 0);
});
