const indexKey = "aiClientNavigationIndex";
const maxKey = "ai-client.navigation-max.v1";

function currentIndex() { return Number.isInteger(history.state?.[indexKey]) ? history.state[indexKey] : 0; }
function maximumIndex() { return Number(sessionStorage.getItem(maxKey) ?? currentIndex()); }
function state() { const index = currentIndex(); return { canBack: index > 0, canForward: index < maximumIndex() }; }

function activeDialog() { return [...document.querySelectorAll('[aria-modal="true"], dialog[open]')].at(-1); }

function rememberWorkspace() {
    if (typeof globalThis.invokeCSharpAction !== "function") return;
    const query = new URL(location.href).searchParams;
    globalThis.invokeCSharpAction(JSON.stringify({
        type: "workspace-location",
        project: query.get("project"),
        chat: query.get("chat"),
        branch: query.get("branch")
    }));
}

export function attach(dotNetReference) {
    if (!Number.isInteger(history.state?.[indexKey])) {
        history.replaceState({ ...history.state, [indexKey]: 0 }, "", location.href);
        sessionStorage.setItem(maxKey, "0");
    }
    let workspaceIndex = currentIndex();
    let restoring = false;
    const onPop = () => {
        const index = currentIndex();
        if (restoring || activeDialog()) {
            // Browser history traversal cannot be cancelled. Restore the entry without sending
            // either the attempted location or its restoration to the workspace.
            if (index !== workspaceIndex) {
                restoring = true;
                history.go(workspaceIndex - index);
            } else {
                restoring = false;
            }
            return;
        }
        workspaceIndex = index;
        rememberWorkspace();
        void dotNetReference.invokeMethodAsync("OnWorkspaceHistoryChanged", location.href, state());
    };
    const onMouse = event => {
        if (event.button !== 3 && event.button !== 4) return;
        const dialog = activeDialog();
        if (!dialog) return;
        event.preventDefault();
        event.stopPropagation();
        if (event.type === "mousedown") {
            dialog.dispatchEvent(new CustomEvent("dialognavigate", { detail: event.button === 3 ? "Back" : "Forward" }));
        }
    };
    window.addEventListener("popstate", onPop);
    const mouseEvents = ["mousedown", "mouseup", "auxclick"];
    for (const type of mouseEvents) window.addEventListener(type, onMouse, true);
    return {
        state,
        url() { return location.href; },
        set(projectId, chatId, branchId, messageId, panel, replace = false) {
            const url = new URL(location.href);
            for (const key of ["project", "chat", "branch", "message", "panel"]) url.searchParams.delete(key);
            if (projectId) url.searchParams.set("project", projectId);
            if (chatId) url.searchParams.set("chat", chatId);
            if (branchId && branchId !== chatId) url.searchParams.set("branch", branchId);
            if (messageId) url.searchParams.set("message", messageId);
            if (panel) url.searchParams.set("panel", panel);
            if (url.href === location.href) return state();
            if (replace) history.replaceState({ ...history.state, [indexKey]: currentIndex() }, "", url);
            else {
                const next = currentIndex() + 1;
                history.pushState({ ...history.state, [indexKey]: next }, "", url);
                sessionStorage.setItem(maxKey, String(next));
            }
            workspaceIndex = currentIndex();
            rememberWorkspace();
            return state();
        },
        back() { if (!activeDialog() && state().canBack) history.back(); },
        forward() { if (!activeDialog() && state().canForward) history.forward(); },
        dispose() {
            window.removeEventListener("popstate", onPop);
            for (const type of mouseEvents) window.removeEventListener(type, onMouse, true);
        }
    };
}
