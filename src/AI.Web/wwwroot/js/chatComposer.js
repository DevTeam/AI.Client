import { attachLayoutCorrection } from "./textCorrection.js";

export function attach(textarea, dotNetReference) {
    // A previous attach on the same textarea can still be live: the page re-attaches on render
    // and tears the old handle down asynchronously, so the two overlap. Two live handlers means
    // every shortcut fires twice — one Up press walking two entries back, one Enter submitting
    // twice — so the incoming attach evicts whatever is already on the element.
    if (textarea.__composerDetach) textarea.__composerDetach();
    const detachCorrection = attachLayoutCorrection(textarea, dotNetReference);

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
            paintSuggestion();
        });

    // Mirrors whether the component shows the slash list of skills, for the same reason as
    // historyActive: the list takes Up/Down/Enter/Tab/Escape, and whether it owns the key has to
    // be decided synchronously. The component pushes the state after every render that changes it.
    let skillListOpen = false;

    const acceptSkill = () => dotNetReference
        .invokeMethodAsync("AcceptSkillSuggestion")
        .then(text => {
            if (text === null || text === undefined) return;
            textarea.value = text;
            textarea.setSelectionRange(text.length, text.length);
            resize();
        });

    const handleSkillList = event => {
        const plain = !event.ctrlKey && !event.altKey && !event.shiftKey && !event.metaKey;
        if ((event.key === "ArrowUp" || event.key === "ArrowDown") && plain) {
            event.preventDefault();
            dotNetReference.invokeMethodAsync("MoveSkillSuggestion", event.key === "ArrowUp" ? -1 : 1);
            return true;
        }
        if ((event.key === "Enter" || event.key === "Tab") && plain) {
            event.preventDefault();
            skillListOpen = false;
            acceptSkill();
            return true;
        }
        if (event.key === "Escape") {
            event.preventDefault();
            event.stopPropagation();
            skillListOpen = false;
            dotNetReference.invokeMethodAsync("DismissSkillSuggestions");
            return true;
        }
        return false;
    };

    // The "@" list works like the slash list, with one difference: Tab on a directory opens it
    // instead of attaching it. The page answers each accept with the new text and caret.
    let mentionListOpen = false;

    const applyTextEdit = edit => {
        if (!edit) return;
        textarea.value = edit.text;
        textarea.setSelectionRange(edit.caret, edit.caret);
        resize();
    };

    const handleMentionList = event => {
        const plain = !event.ctrlKey && !event.altKey && !event.shiftKey && !event.metaKey;
        if ((event.key === "ArrowUp" || event.key === "ArrowDown") && plain) {
            event.preventDefault();
            dotNetReference.invokeMethodAsync("MoveMentionSuggestion", event.key === "ArrowUp" ? -1 : 1);
            return true;
        }
        if ((event.key === "Enter" || event.key === "Tab") && plain) {
            event.preventDefault();
            mentionListOpen = false;
            dotNetReference
                .invokeMethodAsync("AcceptMentionSuggestion", textarea.value, textarea.selectionStart, event.key === "Tab")
                .then(applyTextEdit);
            return true;
        }
        if (event.key === "Escape") {
            event.preventDefault();
            event.stopPropagation();
            mentionListOpen = false;
            dotNetReference.invokeMethodAsync("DismissMentionSuggestions");
            return true;
        }
        return false;
    };

    // Every edit and caret move goes to the page while "@" is in the text, so the list follows
    // the word the caret is in. Without an "@" there is nothing to follow, only a list to close.
    let caretReported = false;
    const reportCaret = () => {
        const mentioned = textarea.value.includes("@");
        if (!mentioned && !caretReported) return;
        caretReported = mentioned;
        dotNetReference.invokeMethodAsync("OnComposerCaret", textarea.value, textarea.selectionStart ?? 0);
    };
    const caretKeys = new Set(["ArrowLeft", "ArrowRight", "Home", "End"]);
    const caretKeyHandler = event => { if (caretKeys.has(event.key)) reportCaret(); };

    // The Host's draft of the user's reply. It is drawn only while the box is empty and nothing
    // else is using it; Tab or → takes it as typed text, Esc puts it aside, Ctrl+Space asks for one.
    const ghost = textarea.parentElement?.querySelector(":scope > .composer-suggestion") ?? null;
    const ghostText = ghost?.querySelector(".composer-suggestion-text") ?? null;
    let suggestion = "";
    const showsSuggestion = () => suggestion.length > 0 && textarea.value.length === 0
        && !historyActive && !skillListOpen && !mentionListOpen;
    const paintSuggestion = () => {
        if (!ghost || !ghostText) return;
        const visible = showsSuggestion();
        textarea.parentElement.classList.toggle("has-reply-suggestion", visible);
        if (!visible) {
            ghost.hidden = true;
            return;
        }
        const style = getComputedStyle(textarea);
        for (const name of MIRRORED) ghost.style[name] = style[name];
        ghost.style.fontStyle = "italic";
        ghost.style.width = `${textarea.clientWidth}px`;
        ghostText.textContent = suggestion;
        ghost.hidden = false;
    };
    const acceptSuggestion = () => {
        const text = suggestion;
        suggestion = "";
        // The page learns the text the way it learns typing, from the input event.
        textarea.value = text;
        textarea.setSelectionRange(text.length, text.length);
        textarea.dispatchEvent(new Event("input", { bubbles: true }));
        textarea.focus();
        dotNetReference.invokeMethodAsync("DismissReplySuggestion");
    };
    const handleSuggestion = event => {
        const plain = !event.ctrlKey && !event.altKey && !event.shiftKey && !event.metaKey;
        if (event.key === " " && event.ctrlKey && !event.altKey && !event.shiftKey && textarea.value.length === 0) {
            event.preventDefault();
            dotNetReference.invokeMethodAsync("RequestReplySuggestion");
            return true;
        }
        if (!showsSuggestion()) return false;
        if ((event.key === "Tab" || event.key === "ArrowRight") && plain) {
            event.preventDefault();
            acceptSuggestion();
            return true;
        }
        if (event.key === "Escape") {
            event.preventDefault();
            event.stopPropagation();
            suggestion = "";
            paintSuggestion();
            dotNetReference.invokeMethodAsync("DismissReplySuggestion");
            return true;
        }
        return false;
    };
    const acceptButton = ghost?.querySelector(".composer-suggestion-accept") ?? null;
    // Pressing the key cap must not take the focus from the box first.
    const keepFocus = event => event.preventDefault();
    const acceptClick = event => {
        event.preventDefault();
        if (showsSuggestion()) acceptSuggestion();
    };
    acceptButton?.addEventListener("mousedown", keepFocus);
    acceptButton?.addEventListener("click", acceptClick);

    const handler = event => {
        if (event.isComposing) {
            pushModifiers(event.ctrlKey, event.altKey, event.shiftKey);
            return;
        }
        if (skillListOpen && handleSkillList(event)) return;
        if (mentionListOpen && handleMentionList(event)) return;
        if (handleSuggestion(event)) return;
        // The skill chip sits before the text: Backspace with the caret at the very start removes
        // it whole, the way a chip inside the text would go.
        if (event.key === "Backspace" && textarea.selectionStart === 0 && textarea.selectionEnd === 0
            && !event.ctrlKey && !event.altKey && !event.shiftKey) {
            dotNetReference.invokeMethodAsync("RemoveDraftSkill");
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

    // The "@" links the draft holds are drawn as pills under the text: a copy of it, laid out the
    // same way behind the transparent textarea, with each link wrapped in a mark. The copy only
    // gives the marks their place; the text itself, the caret and the selection stay the
    // textarea's. A mark cannot be wider than its text, so it gets no padding and no icon.
    const highlight = textarea.parentElement?.querySelector(":scope > .composer-mention-highlight") ?? null;
    let mentionTokens = [];
    const MIRRORED = ["fontFamily", "fontSize", "fontWeight", "fontStyle", "letterSpacing", "wordSpacing", "lineHeight",
        "tabSize", "textIndent", "textTransform", "paddingTop", "paddingRight", "paddingBottom", "paddingLeft"];
    // The same word boundaries as MentionLinkWriter, which makes these links of the sent text.
    const standsAlone = (text, index) => index === 0 || /\s/.test(text[index - 1]) || text[index - 1] === "(" || text[index - 1] === '"';
    const endsWord = (text, end) => end === text.length || /[\s.,;:!?)"]/.test(text[end]);
    const paintMentions = () => {
        if (!highlight) return;
        const text = textarea.value;
        const fragment = document.createDocumentFragment();
        let offset = 0;
        if (mentionTokens.length > 0) {
            for (let index = text.indexOf("@"); index >= 0; index = text.indexOf("@", index + 1)) {
                if (index < offset || !standsAlone(text, index)) continue;
                const token = mentionTokens.find(item => text.startsWith(item, index) && endsWord(text, index + item.length));
                if (!token) continue;
                fragment.append(text.slice(offset, index));
                const mark = document.createElement("mark");
                mark.className = "composer-mention";
                mark.textContent = token;
                fragment.append(mark);
                offset = index + token.length;
            }
        }
        if (offset === 0) {
            highlight.replaceChildren();
            highlight.hidden = true;
            return;
        }
        // A trailing line break takes a line in the textarea and none in a block without it.
        fragment.append(text.slice(offset) + "​");
        const style = getComputedStyle(textarea);
        for (const name of MIRRORED) highlight.style[name] = style[name];
        highlight.style.width = `${textarea.clientWidth}px`;
        highlight.style.height = `${textarea.clientHeight}px`;
        highlight.replaceChildren(fragment);
        highlight.hidden = false;
        highlight.scrollTop = textarea.scrollTop;
    };
    const syncScroll = () => { if (highlight && !highlight.hidden) highlight.scrollTop = textarea.scrollTop; };
    textarea.addEventListener("scroll", syncScroll);
    // A narrower composer wraps the text again.
    const resizeObserver = highlight ? new ResizeObserver(() => paintMentions()) : null;
    resizeObserver?.observe(textarea);

    const resize = () => {
        const style = getComputedStyle(textarea);
        const parsedLineHeight = Number.parseFloat(style.lineHeight);
        const lineHeight = Number.isFinite(parsedLineHeight)
            ? parsedLineHeight
            : Number.parseFloat(style.fontSize) * 1.2;
        const maxHeight = lineHeight * 14;
        textarea.style.height = "auto";
        textarea.style.maxHeight = `${maxHeight}px`;
        // Every change of the text from here, typed or set, comes through resize.
        paintSuggestion();
        // A draft longer than the empty box makes room for itself, as typed text would.
        const content = Math.max(textarea.scrollHeight, ghost && !ghost.hidden ? ghost.scrollHeight : 0);
        textarea.style.height = `${Math.min(content, maxHeight)}px`;
        textarea.style.overflowY = textarea.scrollHeight > maxHeight + 1 ? "auto" : "hidden";
        paintMentions();
    };
    textarea.addEventListener("input", resize);
    textarea.addEventListener("input", reportCaret);
    textarea.addEventListener("keyup", caretKeyHandler);
    textarea.addEventListener("click", reportCaret);
    resize();

    const detach = () => {
        detachCorrection();
        textarea.removeEventListener("keydown", handler);
        textarea.removeEventListener("keyup", releaseHandler);
        textarea.removeEventListener("blur", blurHandler);
        textarea.removeEventListener("input", resize);
        textarea.removeEventListener("input", reportCaret);
        textarea.removeEventListener("keyup", caretKeyHandler);
        textarea.removeEventListener("click", reportCaret);
        textarea.removeEventListener("scroll", syncScroll);
        acceptButton?.removeEventListener("mousedown", keepFocus);
        acceptButton?.removeEventListener("click", acceptClick);
        resizeObserver?.disconnect();
        if (textarea.__composerDetach === detach) delete textarea.__composerDetach;
    };
    textarea.__composerDetach = detach;

    return {
        prepareForSend: () => detachCorrection.prepareForSend(),
        focus: () => {
            resize();
            textarea.focus();
        },
        resize,
        setSkillList: open => { skillListOpen = open; paintSuggestion(); },
        setMentionList: open => { mentionListOpen = open; paintSuggestion(); },
        setSuggestion: text => {
            suggestion = text ?? "";
            resize();
        },
        setMentionTokens: tokens => {
            // The longer link first: "@src/app/" must not be taken for the start of "@src/app/x.cs".
            mentionTokens = [...(tokens ?? [])].filter(token => token.length > 1).sort((left, right) => right.length - left.length);
            paintMentions();
        },
        setText: (text, caret) => {
            applyTextEdit({ text, caret });
            textarea.focus();
        },
        // Text put in as if typed at the caret: the input event is what the component and the
        // "@" caret tracking listen to, so a typed "@" or "/" opens its list here as well. An "@"
        // is kept apart from the word before it, or it would not start a mention.
        type: text => {
            const start = textarea.selectionStart ?? textarea.value.length;
            const end = textarea.selectionEnd ?? start;
            const before = textarea.value.slice(0, start);
            const insert = text === "@" && before && !/\s$/.test(before) ? ` ${text}` : text;
            textarea.focus();
            textarea.value = before + insert + textarea.value.slice(end);
            const caret = start + insert.length;
            textarea.setSelectionRange(caret, caret);
            textarea.dispatchEvent(new Event("input", { bubbles: true }));
            resize();
        },
        scrollSkillSuggestionIntoView: () => {
            textarea.closest(".workspace-composer")
                ?.querySelector(".skill-command-item.selected")
                ?.scrollIntoView({ block: "nearest" });
        },
        reset: () => {
            // The bound _chat.Message may take one render to reach the DOM textarea. For Ctrl+Enter
            // (queue) the render that follows is gated on a snapshot push from the dispatcher,
            // which doesn't always fire — so the user would see their text still there. Force
            // a clear here, then let the next resize run naturally.
            textarea.value = "";
            historyActive = false;
            skillListOpen = false;
            mentionListOpen = false;
            caretReported = false;
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

    // Every path the app shows — a file a run changed, a directory of the project, a file link in
    // the transcript — drags the same way: as a file URI, which the drop below already reads and
    // other apps understand too, under one picture shaped like the composer's resource chip.
    const toFileUri = path => {
        const encode = value => value.split("/").map(encodeURIComponent).join("/");
        const slashed = path.replaceAll("\\", "/");
        if (slashed.startsWith("//")) return `file:${encode(slashed)}`;
        if (/^[a-z]:\//i.test(slashed)) return `file:///${slashed.slice(0, 2)}${encode(slashed.slice(2))}`;
        return `file://${encode(slashed)}`;
    };
    const dragSourceOf = target => {
        const element = target instanceof Element ? target.closest("[data-drag-path], [data-file-path]") : null;
        const path = element?.dataset.dragPath ?? element?.dataset.filePath;
        if (!path) return null;
        const declared = element.dataset.dragKind ?? element.dataset.fileKind;
        const kind = declared === "Directory" || !declared && /[\\/]$/.test(path) ? "Directory" : "File";
        return { element, path, kind };
    };
    const showDragImage = (transfer, source) => {
        const chip = document.createElement("div");
        chip.className = "resource-pill path-drag-image";
        chip.innerHTML = `<span class="resource-pill-target mention-${source.kind.toLowerCase()}"><span class="resource-pill-label"></span></span>`;
        chip.querySelector(".resource-pill-label").textContent = source.path.replace(/[\\/]+$/, "").split(/[\\/]/).pop() || source.path;
        document.body.append(chip);
        transfer.setDragImage(chip, 14, Math.round(chip.offsetHeight / 2));
        // The browser takes its snapshot while this event runs; the element is not needed after.
        window.setTimeout(() => chip.remove(), 0);
    };
    let dragSource = null;
    const onDragStart = event => {
        const source = dragSourceOf(event.target);
        if (!source || !event.dataTransfer) return;
        event.dataTransfer.clearData();
        event.dataTransfer.setData("text/uri-list", toFileUri(source.path));
        event.dataTransfer.setData("text/plain", source.path);
        event.dataTransfer.effectAllowed = "copy";
        showDragImage(event.dataTransfer, source);
        dragSource = source.element;
        dragSource.classList.add("path-drag-source");
    };
    const onDragEnd = () => {
        dragSource?.classList.remove("path-drag-source");
        dragSource = null;
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
    document.addEventListener("dragstart", onDragStart);
    document.addEventListener("dragend", onDragEnd);

    return {
        dispose: () => {
            disposed = true;
            clearTimeout(unsupportedTimer);
            conversation.removeEventListener("dragenter", onEnter);
            conversation.removeEventListener("dragover", onOver);
            conversation.removeEventListener("dragleave", onLeave);
            conversation.removeEventListener("drop", onDrop);
            window.removeEventListener("ai-client-files-dropped", onNativeDrop);
            document.removeEventListener("dragstart", onDragStart);
            document.removeEventListener("dragend", onDragEnd);
            onDragEnd();
            clearHighlight();
        }
    };
}

export function scrollToBottom(element) {
    element.scrollTop = element.scrollHeight;
}
