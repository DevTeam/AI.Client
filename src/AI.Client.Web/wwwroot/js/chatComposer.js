export function attach(textarea, dotNetReference) {
    let sending = false;
    let lastCtrl = false;
    let lastAlt = false;

    const pushModifiers = (ctrl, alt) => {
        if (ctrl === lastCtrl && alt === lastAlt) return;
        lastCtrl = ctrl;
        lastAlt = alt;
        dotNetReference.invokeMethodAsync("OnComposerModifiersChanged", ctrl, alt);
    };

    const handler = event => {
        if (event.isComposing || event.key !== "Enter" || event.shiftKey) {
            pushModifiers(event.ctrlKey, event.altKey);
            return;
        }
        event.preventDefault();
        sending = true;
        const method = event.ctrlKey && event.altKey
            ? "ForkFromKeyboard"
            : event.ctrlKey
                ? "QueueFromKeyboard"
                : "SendFromKeyboard";
        dotNetReference.invokeMethodAsync(method).finally(() => { sending = false; });
    };

    const releaseHandler = event => {
        if (event.key !== "Control" && event.key !== "Alt") return;
        // After Ctrl/Alt release the textarea no longer reports them on the next keydown
        // until the user presses them again — read modifiers from the event itself.
        pushModifiers(event.ctrlKey, event.altKey);
    };

    const blurHandler = () => pushModifiers(false, false);

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
