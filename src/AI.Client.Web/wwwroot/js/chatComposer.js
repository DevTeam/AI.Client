export function attach(textarea, dotNetReference) {
    // A previous attach on the same textarea can still be live: the page re-attaches on render
    // and tears the old handle down asynchronously, so the two overlap. Two live handlers means
    // every shortcut fires twice — one Up press walking two entries back, one Enter submitting
    // twice — so the incoming attach evicts whatever is already on the element.
    if (textarea.__composerDetach) textarea.__composerDetach();

    let sending = false;
    let lastCtrl = false;
    let lastAlt = false;
    let lastShift = false;

    const pushModifiers = (ctrl, alt, shift) => {
        if (ctrl === lastCtrl && alt === lastAlt && shift === lastShift) return;
        lastCtrl = ctrl;
        lastAlt = alt;
        lastShift = shift;
        dotNetReference.invokeMethodAsync("OnComposerModifiersChanged", ctrl, alt, shift);
    };

    // Mirrors the .NET navigator's "am I browsing history" flag. The Escape handler has to decide
    // synchronously whether it owns the key, and the answer only ever changes as a result of a
    // call made here, so a local mirror is always in step with the component.
    let historyActive = false;

    const applyHistoryText = text => {
        if (text === null || text === undefined) return;
        // Same reason as reset() below: the bound value takes a render to reach the DOM, and the
        // render is not guaranteed to happen before the user presses the key again. Writing the
        // textarea directly also puts the caret at the end, which is where it belongs after a
        // history entry lands.
        textarea.value = text;
        textarea.setSelectionRange(text.length, text.length);
        resize();
    };

    const moveHistory = direction => dotNetReference
        .invokeMethodAsync("MoveComposerHistory", direction)
        .then(result => {
            if (!result) return;
            historyActive = result.active;
            applyHistoryText(result.text);
        });

    const handler = event => {
        if (event.isComposing) {
            pushModifiers(event.ctrlKey, event.altKey, event.shiftKey);
            return;
        }
        if (event.key === "ArrowUp" || event.key === "ArrowDown") {
            pushModifiers(event.ctrlKey, event.altKey, event.shiftKey);
            if (event.ctrlKey || event.altKey || event.shiftKey) return;
            // History takes the arrow key only at the edge line — Up on the first line, Down on
            // the last — so a multi-line message stays navigable line by line. Deliberately the
            // edge LINE and not the very first/last character: an entry lands with the caret at
            // its end, and requiring the caret to reach position 0 first would make every second
            // Up press do nothing but move the caret on a one-line entry.
            const value = textarea.value;
            const onFirstLine = !value.slice(0, textarea.selectionStart).includes("\n");
            const onLastLine = !value.slice(textarea.selectionEnd).includes("\n");
            if (event.key === "ArrowUp" ? !onFirstLine : !(historyActive && onLastLine)) return;
            event.preventDefault();
            moveHistory(event.key === "ArrowUp" ? -1 : 1);
            return;
        }
        if (event.key === "Escape") {
            pushModifiers(event.ctrlKey, event.altKey, event.shiftKey);
            if (!historyActive) return;
            // Swallowed so the first Escape means "put my own text back" and nothing else picks
            // it up as "close whatever is open".
            event.preventDefault();
            event.stopPropagation();
            historyActive = false;
            dotNetReference.invokeMethodAsync("ExitComposerHistory").then(applyHistoryText);
            return;
        }
        if (event.key !== "Enter") {
            pushModifiers(event.ctrlKey, event.altKey, event.shiftKey);
            return;
        }
        historyActive = false;
        // Shift+Enter is a newline, but Ctrl+Shift+Enter is "interrupt and send now" — so the
        // combination has to be recognised before Shift is treated as "the user is typing".
        const interrupting = event.ctrlKey && event.shiftKey && !event.altKey;
        if (event.shiftKey && !interrupting) {
            pushModifiers(event.ctrlKey, event.altKey, event.shiftKey);
            return;
        }
        event.preventDefault();
        sending = true;
        const method = interrupting
            ? "SendNowFromKeyboard"
            : event.ctrlKey && event.altKey
                ? "ForkFromKeyboard"
                : event.ctrlKey
                    ? "QueueFromKeyboard"
                    : "SendFromKeyboard";
        dotNetReference.invokeMethodAsync(method).finally(() => { sending = false; });
    };

    const releaseHandler = event => {
        if (event.key !== "Control" && event.key !== "Alt" && event.key !== "Shift") return;
        // After Ctrl/Alt/Shift release the textarea no longer reports them on the next keydown
        // until the user presses them again — read modifiers from the event itself.
        pushModifiers(event.ctrlKey, event.altKey, event.shiftKey);
    };

    const blurHandler = () => pushModifiers(false, false, false);

    textarea.addEventListener("keydown", handler);
    textarea.addEventListener("keyup", releaseHandler);
    textarea.addEventListener("blur", blurHandler);

    const resize = () => {
        const style = getComputedStyle(textarea);
        const parsedLineHeight = Number.parseFloat(style.lineHeight);
        const lineHeight = Number.isFinite(parsedLineHeight)
            ? parsedLineHeight
            : Number.parseFloat(style.fontSize) * 1.2;
        const maxHeight = lineHeight * 14;
        textarea.style.height = "auto";
        textarea.style.maxHeight = `${maxHeight}px`;
        textarea.style.height = `${Math.min(textarea.scrollHeight, maxHeight)}px`;
        textarea.style.overflowY = textarea.scrollHeight > maxHeight + 1 ? "auto" : "hidden";
    };
    textarea.addEventListener("input", resize);
    resize();

    const detach = () => {
        textarea.removeEventListener("keydown", handler);
        textarea.removeEventListener("keyup", releaseHandler);
        textarea.removeEventListener("blur", blurHandler);
        textarea.removeEventListener("input", resize);
        if (textarea.__composerDetach === detach) delete textarea.__composerDetach;
    };
    textarea.__composerDetach = detach;

    return {
        focus: () => {
            resize();
            textarea.focus();
        },
        resize,
        reset: () => {
            // The bound _chat.Message may take one render to reach the DOM textarea. For Ctrl+Enter
            // (queue) the render that follows is gated on a snapshot push from the dispatcher,
            // which doesn't always fire — so the user would see their text still there. Force
            // a clear here, then let the next resize run naturally.
            textarea.value = "";
            historyActive = false;
            resize();
        },
        dispose: detach
    };
}

export function copyText(text) {
    return navigator.clipboard.writeText(text);
}

/**
 * File references need an absolute workspace path; a browser File alone does not expose one.
 * Under WebView2 the dropped FileList goes to the desktop host, which reads each file's path and
 * answers with the custom event; URI-list drags work in hosts that expose file URLs. Nothing is
 * uploaded from the dropped files.
 */
export function watchResourceDrop(dotNetReference) {
    const conversation = document.querySelector(".workspace-conversation");
    const composer = conversation?.querySelector(".workspace-composer");
    if (!conversation || !composer) return { dispose: () => {} };

    let dragDepth = 0;
    let unsupportedTimer = 0;
    let disposed = false;
    let pendingNames = null;

    const hasFiles = transfer => Array.from(transfer?.types ?? []).some(type =>
        type === "Files" || type === "text/uri-list" || type === "application/x-moz-file");

    const toPath = value => {
        if (!value) return null;
        const trimmed = value.trim();
        if (/^[a-z]:[\\/]/i.test(trimmed) || trimmed.startsWith("\\\\")) return trimmed;
        try {
            const url = new URL(trimmed);
            if (url.protocol !== "file:") return null;
            let path = decodeURIComponent(url.pathname);
            if (url.hostname) return `\\\\${url.hostname}${path.replaceAll("/", "\\")}`;
            if (/^\/[a-z]:\//i.test(path)) return path.slice(1).replaceAll("/", "\\");
            return path;
        } catch {
            return null;
        }
    };

    const pathsFromTransfer = transfer => {
        const paths = [];
        for (const file of Array.from(transfer?.files ?? [])) {
            const path = toPath(file.path) ?? toPath(file.localPath);
            if (path) paths.push(path);
        }
        for (const type of ["text/uri-list", "text/plain"]) {
            const text = transfer?.getData(type);
            if (!text) continue;
            for (const line of text.split(/\r?\n/)) {
                const value = line.trim();
                if (!value || value.startsWith("#")) continue;
                const path = toPath(value);
                if (path) paths.push(path);
            }
        }
        return [...new Set(paths)];
    };

    const clearHighlight = () => {
        dragDepth = 0;
        composer.classList.remove("resource-drop-active");
    };
    const onEnter = event => {
        if (!hasFiles(event.dataTransfer)) return;
        event.preventDefault();
        dragDepth++;
        composer.classList.add("resource-drop-active");
    };
    const onOver = event => {
        if (!hasFiles(event.dataTransfer)) return;
        event.preventDefault();
        if (event.dataTransfer) event.dataTransfer.dropEffect = "copy";
        composer.classList.add("resource-drop-active");
    };
    const onLeave = event => {
        if (!hasFiles(event.dataTransfer)) return;
        if (conversation.contains(event.relatedTarget)) return;
        clearHighlight();
    };
    const onDrop = event => {
        if (!hasFiles(event.dataTransfer)) return;
        event.preventDefault();
        clearHighlight();
        const paths = pathsFromTransfer(event.dataTransfer);
        if (paths.length) {
            clearTimeout(unsupportedTimer);
            void dotNetReference.invokeMethodAsync("OnComposerFilesDropped", paths);
            return;
        }
        const files = event.dataTransfer?.files ?? [];
        const names = Array.from(files).map(file => file.name).filter(Boolean);
        const unavailable = () => {
            pendingNames = null;
            if (!disposed) void dotNetReference.invokeMethodAsync("OnComposerFileDropUnavailable", names);
        };
        clearTimeout(unsupportedTimer);
        const desktop = globalThis.chrome?.webview;
        if (files.length && typeof desktop?.postMessageWithAdditionalObjects === "function") {
            pendingNames = names;
            desktop.postMessageWithAdditionalObjects(JSON.stringify({ type: "dropped-files" }), files);
            // A host without the bridge never answers; say so rather than drop the files silently.
            unsupportedTimer = window.setTimeout(unavailable, 2000);
            return;
        }
        unavailable();
    };
    const onNativeDrop = event => {
        if (pendingNames === null) return;
        const names = pendingNames;
        pendingNames = null;
        clearTimeout(unsupportedTimer);
        const paths = Array.isArray(event.detail) ? event.detail.map(toPath).filter(Boolean) : [];
        if (disposed) return;
        if (paths.length) void dotNetReference.invokeMethodAsync("OnComposerFilesDropped", [...new Set(paths)]);
        else void dotNetReference.invokeMethodAsync("OnComposerFileDropUnavailable", names);
    };

    conversation.addEventListener("dragenter", onEnter);
    conversation.addEventListener("dragover", onOver);
    conversation.addEventListener("dragleave", onLeave);
    conversation.addEventListener("drop", onDrop);
    window.addEventListener("ai-client-files-dropped", onNativeDrop);

    return {
        dispose: () => {
            disposed = true;
            clearTimeout(unsupportedTimer);
            conversation.removeEventListener("dragenter", onEnter);
            conversation.removeEventListener("dragover", onOver);
            conversation.removeEventListener("dragleave", onLeave);
            conversation.removeEventListener("drop", onDrop);
            window.removeEventListener("ai-client-files-dropped", onNativeDrop);
            clearHighlight();
        }
    };
}

export function scrollToBottom(element) {
    element.scrollTop = element.scrollHeight;
}
