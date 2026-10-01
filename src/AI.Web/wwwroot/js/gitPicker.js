export function attach(dialog, dotNetReference) {
    const previous = document.activeElement;
    const controls = () => Array.from(dialog.querySelectorAll('button, input, select, [tabindex="0"]'))
        .filter(element => !element.disabled && element.getClientRects().length > 0);
    const handler = event => {
        if (!dialog.isConnected || event.isComposing) return;
        if (event.key === "Escape") {
            event.preventDefault();
            event.stopPropagation();
            dotNetReference.invokeMethodAsync("CloseFromKeyboardAsync");
        } else if (event.key === "Tab") {
            const items = controls();
            const index = items.indexOf(document.activeElement);
            if (index < 0 || (!event.shiftKey && index === items.length - 1) || (event.shiftKey && index === 0)) {
                event.preventDefault();
                (event.shiftKey ? items.at(-1) : items[0])?.focus();
            }
        }
    };
    document.addEventListener("keydown", handler, true);
    dialog.querySelector(".directory-picker-filter")?.focus();
    return {
        dispose: () => {
            document.removeEventListener("keydown", handler, true);
            if (previous instanceof HTMLElement && previous.isConnected) previous.focus();
        }
    };
}
