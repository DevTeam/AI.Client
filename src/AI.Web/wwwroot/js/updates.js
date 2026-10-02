(function () {
    let sequence = 0;
    const pending = new Map();
    window.aiClientUpdates = {
        isDesktop: () => typeof globalThis.invokeCSharpAction === "function",
        request: (operation, preferences) => new Promise((resolve, reject) => {
            const id = ++sequence;
            const timer = setTimeout(() => {
                pending.delete(id);
                reject(new Error("The Desktop update request timed out."));
            }, operation === "state" ? 15000 : 1800000);
            pending.set(id, { resolve, reject, timer });
            globalThis.invokeCSharpAction(JSON.stringify({ type: "update-request", id, operation, preferences }));
        }),
        receive: (id, result) => {
            const request = pending.get(id);
            if (!request) return;
            clearTimeout(request.timer);
            pending.delete(id);
            if (!result.product) request.reject(new Error(result.error || "Invalid update response."));
            else request.resolve(result);
        },
        reload: () => location.reload(),
        freeze: frozen => {
            if (frozen && document.activeElement instanceof HTMLElement) document.activeElement.blur();
            const workspace = document.querySelector(".workspace-shell");
            if (workspace) workspace.inert = frozen;
        }
    };
})();
