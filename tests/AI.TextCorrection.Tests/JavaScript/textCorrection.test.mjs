import { readFileSync } from "node:fs";
import { createContext, runInContext } from "node:vm";
import { test } from "node:test";
import assert from "node:assert/strict";

const source = readFileSync(new URL("../../../src/AI.Web/wwwroot/js/textCorrection.js", import.meta.url), "utf8").replace("export function", "function");

function fixture(analyze = async () => [{ start: 0, length: 6, text: "привет" }], initial = "ghbdtn ", ambiguous = ",.;:'\"[]{}<>`~") {
    const listeners = new Map();
    const timers = new Map();
    let id = 0, calls = 0, changes = 0;
    const textarea = {
        value: "", selectionStart: 0, selectionEnd: 0,
        addEventListener(name, handler) { if (!listeners.has(name)) listeners.set(name, new Set()); listeners.get(name).add(handler); },
        removeEventListener(name, handler) { listeners.get(name)?.delete(handler); },
        setSelectionRange(start, end) { this.selectionStart = start; this.selectionEnd = end; },
        dispatchEvent(event) { if (event.type === "input") changes++; for (const handler of listeners.get(event.type) || []) handler(event); }
    };
    const context = createContext({ console, Event, document: { activeElement: textarea },
        setTimeout(callback) { timers.set(++id, callback); return id; }, clearTimeout(key) { timers.delete(key); } });
    runInContext(source, context);
    const requests = [];
    const detach = context.attachLayoutCorrection(textarea, { async invokeMethodAsync(method, text, excluded) {
        if (method === "GetComposerAmbiguousSeparators") return ambiguous;
        calls++; requests.push({ text, excluded }); assert.equal(method, "AnalyzeComposerLayout"); return analyze(text, excluded);
    } });
    const input = (inputType = "insertText", isComposing = false, data = " ") => textarea.dispatchEvent({ type: "input", inputType, isComposing, data });
    const flush = async () => { const pending = [...timers.values()]; timers.clear(); await Promise.all(pending.map(callback => callback())); };
    const edit = (start, end, text, inputType = "insertText") => {
        textarea.setSelectionRange(start, end);
        if (inputType.startsWith("insertFromPaste")) textarea.dispatchEvent({ type: "paste" });
        textarea.dispatchEvent({ type: "beforeinput", inputType });
        textarea.value = textarea.value.slice(0, start) + text + textarea.value.slice(end);
        textarea.setSelectionRange(start + text.length, start + text.length);
        input(inputType, false, text);
    };
    if (initial) { edit(0, 0, initial); timers.clear(); changes = 0; }
    return { textarea, context, input, flush, detach, edit, requests, calls: () => calls, changes: () => changes };
}

test("corrects typed text, keeps caret, and emits input for Blazor", async () => {
    const f = fixture(); f.input(); await f.flush();
    assert.equal(f.textarea.value, "привет ");
    assert.equal(f.textarea.selectionStart, 7);
    assert.equal(f.changes(), 2);
    assert.equal(f.calls(), 1);
});

test("Ctrl+Z restores the correction and suppresses repeated replacement", async () => {
    const f = fixture(); f.input(); await f.flush();
    let prevented = false;
    f.textarea.dispatchEvent({ type: "keydown", key: "z", ctrlKey: true, preventDefault() { prevented = true; }, stopImmediatePropagation() {} });
    assert.equal(prevented, true); assert.equal(f.textarea.value, "ghbdtn ");
    f.input(); await f.flush(); assert.equal(f.textarea.value, "ghbdtn ");
});

test("does not modify pastes, native undo, or IME composition", async () => {
    for (const type of ["insertFromPaste", "historyUndo"]) {
        const f = fixture(); f.input(type); await f.flush(); assert.equal(f.calls(), 0);
    }
    const f = fixture(); f.textarea.dispatchEvent({ type: "compositionstart" }); f.input("insertText", true); await f.flush();
    assert.equal(f.calls(), 0);
});

test("ignores a result when text changes during analysis", async () => {
    let finish;
    const f = fixture(() => new Promise(resolve => { finish = resolve; }));
    f.input(); const pending = f.flush(); await new Promise(setImmediate);
    f.textarea.value = "ghbdtn!"; f.input();
    finish([{ start: 0, length: 6, text: "привет" }]); await pending;
    assert.equal(f.textarea.value, "ghbdtn!");
});

test("ignores a result when the caret moves during analysis", async () => {
    let finish;
    const f = fixture(() => new Promise(resolve => { finish = resolve; }));
    f.input(); const pending = f.flush(); await new Promise(setImmediate); f.textarea.setSelectionRange(1, 1);
    finish([{ start: 0, length: 6, text: "привет" }]); await pending;
    assert.equal(f.textarea.value, "ghbdtn ");
});

test("detach cancels pending work and removes event handlers", async () => {
    const f = fixture(); f.input(); f.detach(); await f.flush(); f.input(); await f.flush();
    assert.equal(f.calls(), 0);
});

test("Enter cancels analysis before submission", async () => {
    const f = fixture(); f.input(); f.textarea.dispatchEvent({ type: "keydown", key: "Enter" }); await f.flush();
    assert.equal(f.calls(), 0);
});

test("leaves selected text and inactive fields alone", async () => {
    const f = fixture(); f.textarea.setSelectionRange(0, 6); f.input(); await f.flush(); assert.equal(f.calls(), 0);
    f.textarea.setSelectionRange(6, 6); f.context.document.activeElement = null; f.input(); await f.flush(); assert.equal(f.calls(), 0);
});

test("does not analyze an unfinished word even when the user pauses", async () => {
    const f = fixture(); f.textarea.value = "ghbdtn"; f.textarea.setSelectionRange(6, 6);
    f.input("insertText", false, "n"); await f.flush(); await f.flush();
    assert.equal(f.calls(), 0); assert.equal(f.textarea.value, "ghbdtn");
});

test("dots and commas inside a mistyped word do not finish it", async () => {
    for (const character of [".", ","]) {
        const f = fixture(); f.textarea.value = "те" + character; f.textarea.setSelectionRange(3, 3);
        f.input("insertText", false, character); await f.flush();
        assert.equal(f.calls(), 0);
    }
});

test("typing another letter preserves correction of the completed word", async () => {
    const f = fixture(); f.input(); f.textarea.value += "s"; f.textarea.setSelectionRange(8, 8);
    f.input("insertText", false, "s"); await f.flush();
    assert.equal(f.calls(), 1); assert.equal(f.textarea.value, "привет s");
});

test("punctuation that maps to letters does not end an unfinished token", async () => {
    for (const character of [".", ",", ";", "'", "["]) {
        const f = fixture(); f.textarea.value = "ghbdtn" + character; f.textarea.setSelectionRange(7, 7);
        f.input("insertText", false, character); await f.flush(); assert.equal(f.calls(), 0);
    }
});

test("a late result corrects the completed prefix without changing the next word", async () => {
    let finish;
    const f = fixture(text => { assert.equal(text, "ghbdtn "); return new Promise(resolve => { finish = resolve; }); });
    f.input(); const pending = f.flush(); await new Promise(setImmediate);
    f.textarea.value += "unfinished"; f.textarea.setSelectionRange(17, 17); f.input("insertText", false, "d");
    finish([{ start: 0, length: 6, text: "привет" }]); await pending;
    assert.equal(f.textarea.value, "привет unfinished"); assert.equal(f.textarea.selectionStart, 17);
});

test("send waits for analysis and includes the final word without a separator", async () => {
    let finish;
    const f = fixture(text => { assert.equal(text, "ghbdtn"); return new Promise(resolve => { finish = resolve; }); });
    f.textarea.value = "ghbdtn"; f.textarea.setSelectionRange(6, 6);
    f.input("insertText", false, "n");
    f.textarea.dispatchEvent({ type: "keydown", key: "Enter" });
    let completed = false;
    const sending = f.detach.prepareForSend().then(text => { completed = true; return text; });
    await Promise.resolve(); assert.equal(completed, false);
    finish([{ start: 0, length: 6, text: "привет" }]);
    assert.equal(await sending, "привет");
});

test("send corrects the entire message even when focus is on the button", async () => {
    const f = fixture(); f.context.document.activeElement = null;
    f.textarea.value = "ghbdtn " + "a".repeat(140); f.textarea.setSelectionRange(147, 147);
    f.input("insertText", false, "a");
    assert.equal(await f.detach.prepareForSend(), "привет " + "a".repeat(140));
});

test("send preserves an explicitly undone correction", async () => {
    const f = fixture(); f.input(); await f.flush();
    f.textarea.dispatchEvent({ type: "keydown", key: "z", ctrlKey: true, preventDefault() {}, stopImmediatePropagation() {} });
    assert.equal(await f.detach.prepareForSend(), "ghbdtn ");
});

test("send aborts when the draft changes while preparation is pending", async () => {
    let finish;
    const f = fixture(() => new Promise(resolve => { finish = resolve; }));
    const sending = f.detach.prepareForSend();
    f.textarea.value += "new"; f.textarea.setSelectionRange(10, 10); f.input("insertText", false, "w");
    finish([{ start: 0, length: 6, text: "привет" }]);
    assert.equal(await sending, null); assert.equal(f.textarea.value, "ghbdtn new");
});

test("newlines complete tokens and only the unfinished suffix is excluded", async () => {
    const f = fixture(text => { assert.equal(text, "ghbdtn\n"); return [{ start: 0, length: 6, text: "привет" }]; });
    f.textarea.value = "ghbdtn\nnext"; f.textarea.setSelectionRange(11, 11);
    f.input("insertText", false, "t"); await f.flush();
    assert.equal(f.textarea.value, "привет\nnext");
});

test("unambiguous punctuation finishes a word while preserving the unfinished suffix", async () => {
    for (const character of ["!", "?", ")", "—", "/"]) {
        const f = fixture(text => { assert.equal(text, "ghbdtn" + character); return [{ start: 0, length: 6, text: "привет" }]; });
        f.textarea.value = "ghbdtn" + character + "next"; f.textarea.setSelectionRange(11, 11);
        f.input("insertText", false, "t"); await f.flush();
        assert.equal(f.textarea.value, "привет" + character + "next");
    }
});

test("pasted words remain excluded after typing a separator and on send", async () => {
    const f = fixture(undefined, "");
    f.edit(0, 0, "ghbdtn", "insertFromPaste"); await f.flush();
    assert.equal(f.calls(), 0);
    f.edit(6, 6, " "); await f.flush();
    assert.equal(f.textarea.value, "ghbdtn ");
    assert.equal(await f.detach.prepareForSend(), "ghbdtn ");
    assert.equal(f.requests.at(-1).excluded[0].length, 6);
});

test("only typed words are corrected in a draft containing pasted words", async () => {
    const f = fixture(async () => [
        { start: 0, length: 6, text: "привет" }, { start: 7, length: 6, text: "привет" }
    ], "");
    f.edit(0, 0, "ghbdtn ", "insertFromPaste");
    f.edit(7, 7, "ghbdtn "); await f.flush();
    assert.equal(f.textarea.value, "ghbdtn привет ");
    assert.equal(await f.detach.prepareForSend(), "ghbdtn привет ");
});

test("a manual edit re-enables the edited pasted word but preserves its neighbours", async () => {
    const f = fixture(async () => [
        { start: 0, length: 6, text: "привет" }, { start: 7, length: 6, text: "привет" }
    ], "");
    f.edit(0, 0, "ghbdtn ghbdtn ", "insertFromPaste");
    f.edit(1, 2, "h"); f.edit(14, 14, " "); await f.flush();
    assert.equal(f.textarea.value, "привет ghbdtn  ");
    assert.equal(await f.detach.prepareForSend(), "привет ghbdtn  ");
});

test("paste replacing a selection with identical text is still excluded", async () => {
    const f = fixture();
    f.edit(0, 6, "ghbdtn", "insertFromPaste");
    assert.equal(await f.detach.prepareForSend(), "ghbdtn ");
});

test("length-changing corrections move pasted ranges and undo restores their positions", async () => {
    const f = fixture(async text => text.startsWith("helo ") ? [
        { start: 0, length: 4, text: "hello" }, { start: 5, length: 6, text: "привет" }
    ] : [], "");
    f.edit(0, 0, "helo ");
    f.edit(5, 5, "ghbdtn ", "insertFromPaste");
    f.edit(12, 12, " "); await f.flush();
    assert.equal(f.textarea.value, "hello ghbdtn  ");
    assert.equal(f.requests.at(-1).excluded[0].index, 5);
    assert.equal(await f.detach.prepareForSend(), "hello ghbdtn  ");
    assert.equal(f.requests.at(-1).excluded[0].index, 6);
    f.textarea.dispatchEvent({ type: "keydown", key: "z", ctrlKey: true, preventDefault() {}, stopImmediatePropagation() {} });
    assert.equal(f.textarea.value, "helo ghbdtn  ");
    assert.equal(await f.detach.prepareForSend(), "helo ghbdtn  ");
    assert.equal(f.requests.at(-1).excluded[0].index, 5);
});

test("partial pastes exclude the whole word and dropped text is protected", async () => {
    for (const type of ["insertFromPaste", "insertFromDrop"]) {
        const f = fixture(undefined, "");
        f.edit(0, 0, "ghb"); f.edit(3, 3, "dtn", type); f.edit(6, 6, " "); await f.flush();
        assert.equal(f.textarea.value, "ghbdtn ");
        assert.equal(await f.detach.prepareForSend(), "ghbdtn ");
        assert.equal(f.requests.at(-1).excluded[0].index, 0);
        assert.equal(f.requests.at(-1).excluded[0].length, 6);
    }
});

test("a paste cancels an outstanding typed-word correction", async () => {
    let finish;
    const f = fixture(() => new Promise(resolve => { finish = resolve; }));
    f.input(); const pending = f.flush(); await new Promise(setImmediate);
    f.edit(0, 6, "ghbdtn", "insertFromPaste");
    finish([{ start: 0, length: 6, text: "привет" }]); await pending;
    assert.equal(f.textarea.value, "ghbdtn ");
});

test("reset clears paste protection and undo suppression for the next message", async () => {
    const f = fixture(undefined, "");
    f.edit(0, 0, "ghbdtn", "insertFromPaste");
    f.textarea.value = ""; f.textarea.setSelectionRange(0, 0); f.detach.reset();
    f.edit(0, 0, "ghbdtn "); await f.flush();
    assert.equal(f.textarea.value, "привет ");
});

test("programmatically replaced text is preserved on send", async () => {
    const f = fixture(undefined, "");
    f.textarea.value = "ghbdtn"; f.textarea.setSelectionRange(6, 6);
    assert.equal(await f.detach.prepareForSend(), "ghbdtn");
});

test("one-language punctuation completes a spelling correction", async () => {
    const f = fixture(async () => [{ start: 0, length: 6, text: "hello" }], "", "");
    f.edit(0, 0, "helllo"); await f.flush(); assert.equal(f.calls(), 0);
    f.edit(6, 6, "."); await f.flush();
    assert.equal(f.textarea.value, "hello.");
});

test("reattaching a restored draft preserves words whose provenance is unknown", async () => {
    const f = fixture(undefined, ""); f.textarea.value = "ghbdtn"; f.textarea.setSelectionRange(6, 6);
    f.detach();
    const restored = f.context.attachLayoutCorrection(f.textarea, { async invokeMethodAsync(method) {
        return method === "GetComposerAmbiguousSeparators" ? "" : [{ start: 0, length: 6, text: "привет" }];
    } });
    f.input("insertText", false, " ");
    assert.equal(await restored.prepareForSend(), "ghbdtn");
    restored();
});
