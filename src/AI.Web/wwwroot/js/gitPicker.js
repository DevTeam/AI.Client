// Keys for the Git picker, taken at the document while it is open for the same reason as in
// directoryPicker.js: a row re-rendered under the focus drops it to the body, and a handler on the
// dialog would never hear Escape or an arrow again. Tab is kept inside the dialog.
export function attach(dialog, dotNetReference) {
    const previous = document.activeElement;
    const filter = () => dialog.querySelector(".directory-picker-filter");
    const controls = () => Array.from(dialog.querySelectorAll("button, input, select, [tabindex=\"0\"]"))
        .filter(element => !element.disabled && element.getClientRects().length > 0);
    const send = (key, source = "", row = null) => dotNetReference.invokeMethodAsync("OnPickerKey", key, source, row);
    const handler = event => {
        if (!dialog.isConnected || event.isComposing) return;
        const target = event.target instanceof Element ? event.target : null;
        const plain = !event.ctrlKey && !event.altKey && !event.shiftKey && !event.metaKey;
        if (event.key === "Escape") {
            event.preventDefault();
            event.stopPropagation();
            send("Escape");
        } else if (event.key === "Tab") {
            const items = controls();
            const index = items.indexOf(document.activeElement);
            if (index < 0 || (!event.shiftKey && index === items.length - 1) || (event.shiftKey && index === 0)) {
                event.preventDefault();
                (event.shiftKey ? items.at(-1) : items[0])?.focus();
            }
        } else if ((event.key === "ArrowUp" || event.key === "ArrowDown") && plain) {
            // The revision box keeps its arrows: they walk its drop-down of branches.
            if (target?.classList.contains("git-picker-revision")) return;
            event.preventDefault();
            // The highlight is the one choice: a row still focused from a click would otherwise
            // answer the next Enter instead of the row the arrows moved to.
            if (target?.closest(".directory-picker-list")) filter()?.focus();
            send(event.key);
        } else if (event.key === "Enter" && (event.ctrlKey || event.metaKey) && !event.altKey) {
            event.preventDefault();
            send("Submit");
        } else if (event.key === "Enter" && plain) {
            // Toolbar and footer buttons keep their own Enter: it is their click.
            if (target?.closest("button") && dialog.contains(target) && !target.closest(".directory-picker-list")) return;
            if (target?.closest(".git-picker-more")) return;
            event.preventDefault();
            const row = target?.closest(".git-picker-row");
            const source = row ? "row" : target?.classList.contains("git-picker-revision") ? "revision" : "";
            send("Enter", source, row?.dataset.value ?? null);
        }
    };
    document.addEventListener("keydown", handler, true);
    filter()?.focus();
    return {
        scrollHighlightIntoView: () => dialog.querySelector(".directory-picker-item.highlighted")
            ?.scrollIntoView({ block: "nearest" }),
        dispose: () => {
            document.removeEventListener("keydown", handler, true);
            if (previous instanceof HTMLElement && previous.isConnected) previous.focus();
        }
    };
}
