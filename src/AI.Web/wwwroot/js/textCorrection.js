// DOM changes stay here; language and layout decisions belong to the injected .NET analyzer.
export function attachLayoutCorrection(textarea, dotNetReference) {
    let timer;
    let generation = 0;
    let disposed = false;
    let composing = false;
    let applying = false;
    let undo = null;
    let requestedPrefix = null;
    const rejected = new Set();
    let observedValue = textarea.value;
    // Restored drafts have no input provenance; preserve them until manually edited.
    let excluded = textarea.value.length ? [{ start: 0, end: textarea.value.length }] : [];
    let beforeChange = null;
    let pasting = false;

    const isLetter = character => !!character && /[\p{L}\p{M}\p{N}_]/u.test(character);
    const subtractRange = (start, end) => {
        excluded = excluded.flatMap(range => {
            if (range.end <= start || range.start >= end) return [range];
            const remaining = [];
            if (range.start < start) remaining.push({ start: range.start, end: start });
            if (range.end > end) remaining.push({ start: end, end: range.end });
            return remaining;
        });
    };
    const moveRanges = (start, removed, inserted, protect) => {
        const end = start + removed, delta = inserted - removed;
        excluded = excluded.flatMap(range => {
            if (range.end <= start) return [range];
            if (range.start >= end) return [{ start: range.start + delta, end: range.end + delta }];
            const remaining = [];
            if (range.start < start) remaining.push({ start: range.start, end: start });
            if (range.end > end) remaining.push({ start: start + inserted, end: range.end + delta });
            return remaining;
        });
        if (protect && inserted > 0) excluded.push({ start, end: start + inserted });
        excluded.sort((a, b) => a.start - b.start);
        excluded = excluded.reduce((merged, range) => {
            const previous = merged.at(-1);
            if (previous && range.start <= previous.end) previous.end = Math.max(previous.end, range.end);
            else merged.push({ ...range });
            return merged;
        }, []);
    };
    const trackChange = (typed, protect) => {
        const original = observedValue, current = textarea.value;
        let start = 0, removed = 0, inserted = 0;
        if (beforeChange?.original === original) {
            start = beforeChange.start;
            removed = beforeChange.end - start;
            inserted = current.length - original.length + removed;
        }
        if (!beforeChange || beforeChange.original !== original || inserted < 0
            || current.slice(0, start) !== original.slice(0, start)
            || current.slice(start + inserted) !== original.slice(start + removed)) {
            start = 0;
            while (start < Math.min(original.length, current.length) && original[start] === current[start]) start++;
            let suffix = 0;
            while (suffix < original.length - start && suffix < current.length - start
                && original[original.length - suffix - 1] === current[current.length - suffix - 1]) suffix++;
            removed = original.length - start - suffix;
            inserted = current.length - start - suffix;
            // A paste can replace a selection with identical text.
            if (protect && removed === 0 && inserted === 0 && current.length > 0) {
                start = 0; removed = current.length; inserted = current.length;
            }
        }
        if (typed && (removed > 0 || [...current.slice(start, start + inserted)].some(isLetter))) {
            let wordStart = start, wordEnd = start + removed;
            while (wordStart > 0 && isLetter(original[wordStart - 1])) wordStart--;
            while (wordEnd < original.length && isLetter(original[wordEnd])) wordEnd++;
            subtractRange(wordStart, wordEnd);
        }
        moveRanges(start, removed, inserted, protect);
        observedValue = current;
        beforeChange = null;
    };
    const synchronize = () => {
        if (textarea.value !== observedValue) { beforeChange = null; trackChange(false, true); }
    };
    const protectedRanges = text => excluded.filter(range => range.start < text.length).map(range => {
        let start = range.start, end = Math.min(range.end, text.length);
        if (isLetter(text[start])) while (start > 0 && isLetter(text[start - 1])) start--;
        if (isLetter(text[end - 1])) while (end < text.length && isLetter(text[end])) end++;
        return { index: start, length: end - start };
    });

    const cancel = () => { clearTimeout(timer); generation++; requestedPrefix = null; };
    const notify = () => {
        applying = true;
        textarea.dispatchEvent(new Event("input", { bubbles: true }));
        applying = false;
    };
    const apply = (original, caret, candidates, end, recentOnly) => {
        const protectedWords = protectedRanges(original);
        const edits = candidates.filter(edit => (!recentOnly || edit.start >= end - 120)
            && edit.start >= 0 && edit.start + edit.length <= end
            && !protectedWords.some(range => edit.start < range.index + range.length && edit.start + edit.length > range.index)
            && !rejected.has(original.slice(edit.start, edit.start + edit.length)));
        if (!edits.length) return original;
        let corrected = original;
        let delta = 0;
        const originalExcluded = excluded.map(range => ({ ...range }));
        for (const edit of [...edits].sort((a, b) => b.start - a.start)) {
            corrected = corrected.slice(0, edit.start) + edit.text + corrected.slice(edit.start + edit.length);
            if (edit.start + edit.length <= caret) delta += edit.text.length - edit.length;
            moveRanges(edit.start, edit.length, edit.text.length, false);
        }
        undo = { original, corrected, caret, excluded: originalExcluded, words: edits.map(edit => original.slice(edit.start, edit.start + edit.length)) };
        textarea.value = corrected;
        observedValue = corrected;
        textarea.setSelectionRange(caret + delta, caret + delta);
        notify();
        return corrected;
    };
    const correct = async () => {
        const revision = generation;
        if (disposed || composing || document.activeElement !== textarea || textarea.selectionStart !== textarea.selectionEnd) return;
        try {
            const ambiguous = await dotNetReference.invokeMethodAsync("GetComposerAmbiguousSeparators");
            if (disposed || revision !== generation || composing || document.activeElement !== textarea
                || textarea.selectionStart !== textarea.selectionEnd) return;
            const caret = textarea.selectionStart;
            // Punctuation may represent a letter in another enabled layout.
            const beforeCaret = textarea.value.slice(0, caret);
            let end = 0;
            for (let index = 0; index < beforeCaret.length; index++) {
                const character = beforeCaret[index];
                if (/\s/u.test(character) || /[\p{P}\p{S}]/u.test(character) && !ambiguous.includes(character)) end = index + 1;
            }
            if (!end) return;
            const prefix = beforeCaret.slice(0, end);
            if (requestedPrefix === prefix) return;
            requestedPrefix = prefix;
            const candidates = await dotNetReference.invokeMethodAsync("AnalyzeComposerLayout", prefix, protectedRanges(prefix));
            if (disposed || revision !== generation || composing || !textarea.value.startsWith(prefix)
                || textarea.selectionStart < end || textarea.selectionStart !== textarea.selectionEnd
                || document.activeElement !== textarea) return;
            // Appending the next word does not invalidate the completed prefix.
            apply(textarea.value, textarea.selectionStart, candidates, end, true);
        } catch (error) {
            requestedPrefix = null;
            if (!disposed) console.warn("Text correction analysis failed", error);
        }
    };
    const input = event => {
        if (applying) return;
        const typed = event.inputType?.startsWith("insertText");
        const lineBreak = event.inputType === "insertLineBreak" || event.inputType === "insertParagraph";
        const pasted = pasting || event.inputType?.startsWith("insertFromPaste") || event.inputType === "insertFromDrop";
        const manual = !pasted && !event.isComposing && !composing && (typed || lineBreak || event.inputType?.startsWith("delete"));
        trackChange(manual, pasted || !manual);
        pasting = false;
        if (event.isComposing || composing || (!typed && !lineBreak)) { cancel(); return; }
        if (pasted) { cancel(); return; }
        clearTimeout(timer);
        timer = setTimeout(correct, 0);
    };
    const restore = event => {
        cancel();
        if (!undo || textarea.value !== undo.corrected) return;
        event.preventDefault();
        event.stopImmediatePropagation();
        textarea.value = undo.original;
        observedValue = undo.original;
        excluded = undo.excluded;
        beforeChange = null;
        textarea.setSelectionRange(undo.caret, undo.caret);
        undo.words.forEach(word => rejected.add(word));
        undo = null;
        notify();
    };
    const keydown = event => {
        if ((event.ctrlKey || event.metaKey) && !event.shiftKey && event.key.toLowerCase() === "z") restore(event);
        else if (event.key === "Enter" || event.key === "Escape" || event.key.startsWith("Arrow") || event.ctrlKey || event.metaKey) cancel();
    };
    const beforeInput = event => {
        if (event.inputType === "historyUndo") { restore(event); return; }
        synchronize();
        beforeChange = { original: textarea.value, start: textarea.selectionStart, end: textarea.selectionEnd };
    };
    const paste = () => { pasting = true; cancel(); };
    const compositionStart = () => { composing = true; cancel(); };
    const compositionEnd = () => { composing = false; cancel(); };
    textarea.addEventListener("input", input);
    textarea.addEventListener("keydown", keydown, true);
    textarea.addEventListener("beforeinput", beforeInput);
    textarea.addEventListener("paste", paste);
    textarea.addEventListener("compositionstart", compositionStart);
    textarea.addEventListener("compositionend", compositionEnd);
    textarea.addEventListener("blur", cancel);
    textarea.addEventListener("click", cancel);
    const detach = () => {
        disposed = true;
        cancel();
        textarea.removeEventListener("input", input);
        textarea.removeEventListener("keydown", keydown, true);
        textarea.removeEventListener("beforeinput", beforeInput);
        textarea.removeEventListener("paste", paste);
        textarea.removeEventListener("compositionstart", compositionStart);
        textarea.removeEventListener("compositionend", compositionEnd);
        textarea.removeEventListener("blur", cancel);
        textarea.removeEventListener("click", cancel);
    };
    detach.prepareForSend = async () => {
        cancel();
        const revision = generation;
        if (disposed || composing) return null;
        synchronize();
        const original = textarea.value;
        const candidates = await dotNetReference.invokeMethodAsync("AnalyzeComposerLayout", original, protectedRanges(original));
        // Never submit a different draft if the user edits or navigates while loading.
        if (disposed || revision !== generation || composing || textarea.value !== original) return null;
        return apply(original, textarea.selectionStart, candidates, original.length, false);
    };
    detach.reset = () => {
        cancel(); observedValue = textarea.value; excluded = []; beforeChange = null; pasting = false; undo = null; rejected.clear();
    };
    return detach;
}
