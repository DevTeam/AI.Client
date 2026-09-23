/**
 * Closes an open model picker on a press anywhere outside it. Blur alone is not enough: the list
 * can be opened from the chevron without the input ever taking focus, and then nothing blurs.
 * The capture phase runs before the target's own handlers, and the press is not swallowed, so the
 * same click still reaches whatever is under the pointer. Only an open list calls back to .NET.
 */
export function attach(root, dotNetReference) {
    const handler = event => {
        const target = event.target instanceof Node ? event.target : null;
        if (!target || root.contains(target) || !root.querySelector(".model-picker-list")) return;
        void dotNetReference.invokeMethodAsync("OnOutsidePress");
    };
    document.addEventListener("pointerdown", handler, true);
    return { dispose: () => document.removeEventListener("pointerdown", handler, true) };
}
