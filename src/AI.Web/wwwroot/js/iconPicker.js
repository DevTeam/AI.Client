/**
 * The skill icon combobox. A press outside an open list closes it, like the model picker: the
 * capture phase runs first and does not swallow the press. The trigger is a button, so its own
 * handling of Enter, Space and the arrows (a click, a scroll of the drawer) is cancelled here and
 * .NET handles those keys instead; Tab and everything else keep their defaults.
 */
const handledKeys = new Set(["ArrowDown", "ArrowUp", "Home", "End", "Enter", " "]);

export function attach(root, dotNetReference) {
    const trigger = root.querySelector(".skill-icon-select-trigger");
    const press = event => {
        const target = event.target instanceof Node ? event.target : null;
        if (!target || root.contains(target) || !root.querySelector(".skill-icon-list")) return;
        void dotNetReference.invokeMethodAsync("OnOutsidePress");
    };
    const keydown = event => {
        if (handledKeys.has(event.key) && !event.altKey && !event.ctrlKey && !event.metaKey) event.preventDefault();
    };
    document.addEventListener("pointerdown", press, true);
    trigger?.addEventListener("keydown", keydown);
    return {
        dispose: () => {
            document.removeEventListener("pointerdown", press, true);
            trigger?.removeEventListener("keydown", keydown);
        }
    };
}

/** Scrolls the highlighted option into the list's view after a keyboard move or an open. */
export function reveal(root) {
    root.querySelector(".skill-icon-option.active")?.scrollIntoView({ block: "nearest" });
}
