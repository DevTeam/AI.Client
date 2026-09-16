export function attach(textarea, dotNetReference) {
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

    const handler = event => {
        if (event.isComposing || event.key !== "Enter") {
            pushModifiers(event.ctrlKey, event.altKey, event.shiftKey);
            return;
        }
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
            resize();
        },
        dispose: () => {
            textarea.removeEventListener("keydown", handler);
            textarea.removeEventListener("keyup", releaseHandler);
            textarea.removeEventListener("blur", blurHandler);
            textarea.removeEventListener("input", resize);
        }
    };
}

export function copyText(text) {
    return navigator.clipboard.writeText(text);
}

export function scrollToBottom(element) {
    element.scrollTop = element.scrollHeight;
}
