// Ctrl+Shift+N (Cmd+Shift+N on macOS) asks the Desktop app for one more window over the same
// workspace. In an ordinary browser Avalonia's bridge is absent and the key is left to the browser.
(function () {
    window.addEventListener("keydown", event => {
        if (typeof globalThis.invokeCSharpAction !== "function") return;
        if (event.repeat || event.altKey || !event.shiftKey || !(event.ctrlKey || event.metaKey)) return;
        if (event.code !== "KeyN") return;
        event.preventDefault();
        event.stopPropagation();
        globalThis.invokeCSharpAction(JSON.stringify({ type: "new-window" }));
    }, true);
})();
