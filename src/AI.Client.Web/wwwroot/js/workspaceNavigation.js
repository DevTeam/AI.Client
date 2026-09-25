const indexKey = "aiClientNavigationIndex";
const maxKey = "ai-client.navigation-max.v1";

function currentIndex() { return Number.isInteger(history.state?.[indexKey]) ? history.state[indexKey] : 0; }
function maximumIndex() { return Number(sessionStorage.getItem(maxKey) ?? currentIndex()); }
function state() { const index = currentIndex(); return { canBack: index > 0, canForward: index < maximumIndex() }; }

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
    const onPop = () => { rememberWorkspace(); void dotNetReference.invokeMethodAsync("OnWorkspaceHistoryChanged", location.href, state()); };
    window.addEventListener("popstate", onPop);
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
            rememberWorkspace();
            return state();
        },
        back() { if (state().canBack) history.back(); },
        forward() { if (state().canForward) history.forward(); },
        dispose() { window.removeEventListener("popstate", onPop); }
    };
}
