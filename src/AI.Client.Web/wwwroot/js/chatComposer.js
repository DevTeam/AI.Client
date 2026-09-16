export function attach(textarea, dotNetReference) {
    // A previous attach on the same textarea can still be live: the page re-attaches on render
    // and tears the old handle down asynchronously, so the two overlap. Two live handlers means
    // every shortcut fires twice — one Up press walking two entries back, one Enter submitting
    // twice — so the incoming attach evicts whatever is already on the element.
    if (textarea.__composerDetach) textarea.__composerDetach();

    let sending = false;
    let lastCtrl = false;
    let lastAlt = false;
    let lastShift = false;

    const pushModifiers = (ctrl, alt, shift) => {
        if (ctrl === lastCtrl && alt === lastAlt && shift === lastShift) return;
        lastCtrl = ctrl;
        lastAlt = alt;
        lastShift = shift;
        dotNetReference.invokeMethodAsync("OnComposerModifiersChanged", ctrl, alt, shift);
    };

    // Mirrors the .NET navigator's "am I browsing history" flag. The Escape handler has to decide
    // synchronously whether it owns the key, and the answer only ever changes as a result of a
    // call made here, so a local mirror is always in step with the component.
    let historyActive = false;

    const applyHistoryText = text => {
        if (text === null || text === undefined) return;
        // Same reason as reset() below: the bound value takes a render to reach the DOM, and the
        // render is not guaranteed to happen before the user presses the key again. Writing the
        // textarea directly also puts the caret at the end, which is where it belongs after a
        // history entry lands.
        textarea.value = text;
        textarea.setSelectionRange(text.length, text.length);
        resize();
    };

    const moveHistory = direction => dotNetReference
        .invokeMethodAsync("MoveComposerHistory", direction)
        .then(result => {
            if (!result) return;
            historyActive = result.active;
            applyHistoryText(result.text);
        });

    const handler = event => {
        if (event.isComposing) {
            pushModifiers(event.ctrlKey, event.altKey, event.shiftKey);
            return;
        }
        if (event.key === "ArrowUp" || event.key === "ArrowDown") {
            pushModifiers(event.ctrlKey, event.altKey, event.shiftKey);
            if (event.ctrlKey || event.altKey || event.shiftKey) return;
            // History takes the arrow key only at the edge line — Up on the first line, Down on
            // the last — so a multi-line message stays navigable line by line. Deliberately the
            // edge LINE and not the very first/last character: an entry lands with the caret at
            // its end, and requiring the caret to reach position 0 first would make every second
            // Up press do nothing but move the caret on a one-line entry.
            const value = textarea.value;
            const onFirstLine = !value.slice(0, textarea.selectionStart).includes("\n");
            const onLastLine = !value.slice(textarea.selectionEnd).includes("\n");
            if (event.key === "ArrowUp" ? !onFirstLine : !(historyActive && onLastLine)) return;
            event.preventDefault();
            moveHistory(event.key === "ArrowUp" ? -1 : 1);
            return;
        }
        if (event.key === "Escape") {
            pushModifiers(event.ctrlKey, event.altKey, event.shiftKey);
            if (!historyActive) return;
            // Swallowed so the first Escape means "put my own text back" and nothing else picks
            // it up as "close whatever is open".
            event.preventDefault();
            event.stopPropagation();
            historyActive = false;
            dotNetReference.invokeMethodAsync("ExitComposerHistory").then(applyHistoryText);
            return;
        }
        if (event.key !== "Enter") {
            pushModifiers(event.ctrlKey, event.altKey, event.shiftKey);
            return;
        }
        historyActive = false;
        // Shift+Enter is a newline, but Ctrl+Shift+Enter is "interrupt and send now" — so the
        // combination has to be recognised before Shift is treated as "the user is typing".
        const interrupting = event.ctrlKey && event.shiftKey && !event.altKey;
        if (event.shiftKey && !interrupting) {
            pushModifiers(event.ctrlKey, event.altKey, event.shiftKey);
            return;
        }
        event.preventDefault();
        sending = true;
        const method = interrupting
            ? "SendNowFromKeyboard"
            : event.ctrlKey && event.altKey
                ? "ForkFromKeyboard"
                : event.ctrlKey
                    ? "QueueFromKeyboard"
                    : "SendFromKeyboard";
        dotNetReference.invokeMethodAsync(method).finally(() => { sending = false; });
    };

    const releaseHandler = event => {
        if (event.key !== "Control" && event.key !== "Alt" && event.key !== "Shift") return;
        // After Ctrl/Alt/Shift release the textarea no longer reports them on the next keydown
        // until the user presses them again — read modifiers from the event itself.
        pushModifiers(event.ctrlKey, event.altKey, event.shiftKey);
    };

    const blurHandler = () => pushModifiers(false, false, false);

    textarea.addEventListener("keydown", handler);
    textarea.addEventListener("keyup", releaseHandler);
    textarea.addEventListener("blur", blurHandler);

    const resize = () => {
        const style = getComputedStyle(textarea);
        const parsedLineHeight = Number.parseFloat(style.lineHeight);
        const lineHeight = Number.isFinite(parsedLineHeight)
            ? parsedLineHeight
            : Number.parseFloat(style.fontSize) * 1.2;
        const maxHeight = lineHeight * 14;
        textarea.style.height = "auto";
        textarea.style.maxHeight = `${maxHeight}px`;
        textarea.style.height = `${Math.min(textarea.scrollHeight, maxHeight)}px`;
        textarea.style.overflowY = textarea.scrollHeight > maxHeight + 1 ? "auto" : "hidden";
    };
    textarea.addEventListener("input", resize);
    resize();

    const detach = () => {
        textarea.removeEventListener("keydown", handler);
        textarea.removeEventListener("keyup", releaseHandler);
        textarea.removeEventListener("blur", blurHandler);
        textarea.removeEventListener("input", resize);
        if (textarea.__composerDetach === detach) delete textarea.__composerDetach;
    };
    textarea.__composerDetach = detach;

    return {
        focus: () => {
            resize();
            textarea.focus();
        },
        resize,
        reset: () => {
            // The bound _chat.Message may take one render to reach the DOM textarea. For Ctrl+Enter
            // (queue) the render that follows is gated on a snapshot push from the dispatcher,
            // which doesn't always fire — so the user would see their text still there. Force
            // a clear here, then let the next resize run naturally.
            textarea.value = "";
            historyActive = false;
            resize();
        },
        dispose: detach
    };
}

export function copyText(text) {
    return navigator.clipboard.writeText(text);
}

export function scrollToBottom(element) {
    element.scrollTop = element.scrollHeight;
}
