// Takes the user to the first field a refused settings save marked, opening the disclosure it waits
// under (Context limits, Prices), so "Not saved" always lands next to the thing to fix.

export function focusFirstInvalid(root) {
    const field = root?.querySelector('[aria-invalid="true"]');
    if (!field) return;
    for (let details = field.closest("details"); details; details = details.parentElement?.closest("details")) {
        details.open = true;
    }
    field.scrollIntoView({ block: "nearest" });
    field.focus({ preventScroll: true });
}

// Brings one section of the Settings page to the top of its list. The jump is instant: the row
// of section names stays in place above it, so there is nothing to follow with the eye.
export function showSection(root, id) {
    root?.querySelector(`#${CSS.escape(id)}`)?.closest(".settings-group")?.scrollIntoView({ block: "start" });
}
