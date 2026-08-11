export function attach(textarea, dotNetReference) {
    let sending = false;
    const handler = event => {
        if (event.isComposing || event.key !== "Enter" || event.shiftKey || sending) return;
        event.preventDefault();
        sending = true;
        dotNetReference.invokeMethodAsync("SendFromKeyboard").finally(() => { sending = false; });
    };
    textarea.addEventListener("keydown", handler);

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
        dispose: () => {
            textarea.removeEventListener("keydown", handler);
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
