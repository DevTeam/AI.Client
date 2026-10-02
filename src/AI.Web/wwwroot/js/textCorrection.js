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

    const cancel = () => { clearTimeout(timer); generation++; requestedPrefix = null; };
    const notify = () => {
        applying = true;
        textarea.dispatchEvent(new Event("input", { bubbles: true }));
        applying = false;
    };
    const apply = (original, caret, candidates, end, recentOnly) => {
        const edits = candidates.filter(edit => (!recentOnly || edit.start >= end - 120)
            && edit.start >= 0 && edit.start + edit.length <= end
            && !rejected.has(original.slice(edit.start, edit.start + edit.length)));
        if (!edits.length) return original;
        let corrected = original;
        let delta = 0;
        for (const edit of [...edits].sort((a, b) => b.start - a.start)) {
            corrected = corrected.slice(0, edit.start) + edit.text + corrected.slice(edit.start + edit.length);
            if (edit.start + edit.length <= caret) delta += edit.text.length - edit.length;
        }
        undo = { original, corrected, caret, words: edits.map(edit => original.slice(edit.start, edit.start + edit.length)) };
        textarea.value = corrected;
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
            const candidates = await dotNetReference.invokeMethodAsync("AnalyzeComposerLayout", prefix);
            if (disposed || revision !== generation || composing || !textarea.value.startsWith(prefix)
                || textarea.selectionStart < end || textarea.selectionStart !== textarea.selectionEnd
                || document.activeElement !== textarea) return;
            // Appending the next word does not invalidate the completed prefix.
            apply(textarea.value, textarea.selectionStart, candidates, end, true);
        } catch (error) {
            requestedPrefix = null;
            if (!disposed) console.warn("Keyboard layout analysis failed", error);
        }
    };
    const input = event => {
        if (applying) return;
        const typed = event.inputType?.startsWith("insertText");
        const lineBreak = event.inputType === "insertLineBreak" || event.inputType === "insertParagraph";
        if (event.isComposing || composing || (!typed && !lineBreak)) { cancel(); return; }
        clearTimeout(timer);
        timer = setTimeout(correct, 0);
    };
    const restore = event => {
        cancel();
        if (!undo || textarea.value !== undo.corrected) return;
        event.preventDefault();
        event.stopImmediatePropagation();
        textarea.value = undo.original;
        textarea.setSelectionRange(undo.caret, undo.caret);
        undo.words.forEach(word => rejected.add(word));
        undo = null;
        notify();
    };
    const keydown = event => {
        if ((event.ctrlKey || event.metaKey) && !event.shiftKey && event.key.toLowerCase() === "z") restore(event);
        else if (event.key === "Enter" || event.key === "Escape" || event.key.startsWith("Arrow") || event.ctrlKey || event.metaKey) cancel();
    };
    const beforeInput = event => { if (event.inputType === "historyUndo") restore(event); };
    const compositionStart = () => { composing = true; cancel(); };
    const compositionEnd = () => { composing = false; cancel(); };
    textarea.addEventListener("input", input);
    textarea.addEventListener("keydown", keydown, true);
    textarea.addEventListener("beforeinput", beforeInput);
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
        textarea.removeEventListener("compositionstart", compositionStart);
        textarea.removeEventListener("compositionend", compositionEnd);
        textarea.removeEventListener("blur", cancel);
        textarea.removeEventListener("click", cancel);
    };
    detach.prepareForSend = async () => {
        cancel();
        const revision = generation;
        if (disposed || composing) return null;
        const original = textarea.value;
        const candidates = await dotNetReference.invokeMethodAsync("AnalyzeComposerLayout", original);
        // Never submit a different draft if the user edits or navigates while loading.
        if (disposed || revision !== generation || composing || textarea.value !== original) return null;
        return apply(original, textarea.selectionStart, candidates, original.length, false);
    };
    return detach;
}
