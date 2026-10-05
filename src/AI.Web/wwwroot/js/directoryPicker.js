// Keys for the directory and file picker. They are taken at the document while the picker is open,
// not on its inputs: opening a folder replaces the button that had the focus, the focus falls to
// the body, and a handler on the dialog would never hear Escape or an arrow again.
export function attach(dialog, dotNetReference) {
    const handler = event => {
        if (!dialog.isConnected || event.isComposing) return;
        const plain = !event.ctrlKey && !event.altKey && !event.shiftKey && !event.metaKey;
        if (event.key === "Escape") {
            event.preventDefault();
            event.stopPropagation();
            dotNetReference.invokeMethodAsync("OnPickerKey", "Escape", "", null);
            return;
        }
        if (event.key === "ArrowUp" && event.altKey && !event.ctrlKey && !event.metaKey) {
            event.preventDefault();
            dotNetReference.invokeMethodAsync("OnPickerKey", "Up", "", null);
            return;
        }
        if ((event.key === "ArrowUp" || event.key === "ArrowDown") && plain) {
            event.preventDefault();
            // The highlight is the one choice: a row still holding the focus from a click would
            // otherwise answer the next Enter instead of the row the arrows moved to.
            if (event.target instanceof Element && event.target.closest(".directory-picker-list"))
                dialog.querySelector(".directory-picker-filter")?.focus();
            dotNetReference.invokeMethodAsync("OnPickerKey", event.key, "", null);
            return;
        }
        if (event.key !== "Enter" || !plain) return;
        // The footer and toolbar buttons keep their own Enter: it is their click.
        const target = event.target instanceof Element ? event.target : null;
        if (target?.closest("button") && dialog.contains(target) && !target.closest(".directory-picker-list")) return;
        event.preventDefault();
        // A row the mouse or Tab put the focus on counts as the highlighted one.
        const row = target?.closest(".directory-picker-item");
        const source = row ? "row" : target?.classList.contains("directory-picker-path") ? "path" : "";
        dotNetReference.invokeMethodAsync("OnPickerKey", "Enter", source, row?.getAttribute("title") ?? null);
    };
    document.addEventListener("keydown", handler, true);
    const navigate = event => dotNetReference.invokeMethodAsync("OnPickerKey", event.detail, "", null);
    dialog.addEventListener("dialognavigate", navigate);

    return {
        // The filter takes the focus after a folder opens, so typing narrows the new level.
        focusFilter: () => dialog.querySelector(".directory-picker-filter")?.focus(),
        scrollHighlightIntoView: () => dialog.querySelector(".directory-picker-item.highlighted")
            ?.scrollIntoView({ block: "nearest" }),
        dispose: () => {
            document.removeEventListener("keydown", handler, true);
            dialog.removeEventListener("dialognavigate", navigate);
        }
    };
}
