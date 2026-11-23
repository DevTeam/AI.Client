// Clipboard and file plumbing for copying Connections, MCP servers and skills out of the settings
// drawer and pasting them back in. The text itself is parsed in .NET; nothing here looks inside it.

export function writeClipboardText(text) {
    return navigator.clipboard.writeText(text);
}

/**
 * The clipboard's text, or null when the browser will not hand it over: it asks for permission,
 * refuses outside a secure context, or some WebViews do not implement it. The import dialog then
 * offers a box to paste into, so null is an ordinary answer rather than an error.
 */
export async function readClipboardText() {
    try {
        if (!navigator.clipboard?.readText) return null;
        return await navigator.clipboard.readText();
    } catch {
        return null;
    }
}

export function downloadText(fileName, text, type = "application/json") {
    download(fileName, new Blob([text], { type }));
}

/** Bytes from .NET arrive as a Uint8Array; skills go out as a .zip this way. */
export function downloadBytes(fileName, bytes, type) {
    download(fileName, new Blob([bytes], { type }));
}

function download(fileName, blob) {
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    link.style.display = "none";
    document.body.appendChild(link);
    link.click();
    link.remove();
    // Revoking at once cancels the download in some browsers; the click only schedules it.
    setTimeout(() => URL.revokeObjectURL(url), 30000);
}

const editable = "input, textarea, select, [contenteditable]:not([contenteditable='false'])";

function isFileDrag(event) {
    return Array.from(event.dataTransfer?.types ?? []).includes("Files");
}

const isArchive = file => /\.zip$/i.test(file.name);

/** Text goes to .NET as a string; a .zip (a set of skills) as its bytes, which only .NET can open. */
function sendFile(file, dotNetReference, textMethod, bytesMethod) {
    if (isArchive(file)) {
        void file.arrayBuffer().then(buffer => dotNetReference.invokeMethodAsync(bytesMethod, new Uint8Array(buffer), file.name));
    } else {
        void file.text().then(text => dotNetReference.invokeMethodAsync(textMethod, text, file.name));
    }
}

/**
 * Ctrl+V anywhere in the drawer outside a field, and a file dropped on it, both open the import
 * preview. The paste event carries its text without the permission prompt readText needs, which
 * is why the shortcut goes through it rather than a keydown handler.
 */
export function watchTransfer(drawer, dotNetReference) {
    const onPaste = event => {
        if (event.target instanceof Element && event.target.closest(editable)) return;
        const text = event.clipboardData?.getData("text/plain");
        if (!text?.trim()) return;
        event.preventDefault();
        void dotNetReference.invokeMethodAsync("OnTransferTextAsync", text, "clipboard");
    };
    const onDragOver = event => {
        if (!isFileDrag(event)) return;
        event.preventDefault();
        event.dataTransfer.dropEffect = "copy";
    };
    const onDrop = event => {
        if (!isFileDrag(event)) return;
        event.preventDefault();
        const file = event.dataTransfer.files?.[0];
        if (file) sendFile(file, dotNetReference, "OnTransferTextAsync", "OnTransferBytesAsync");
    };
    drawer.addEventListener("paste", onPaste);
    drawer.addEventListener("dragover", onDragOver);
    drawer.addEventListener("drop", onDrop);
    return {
        dispose: () => {
            drawer.removeEventListener("paste", onPaste);
            drawer.removeEventListener("dragover", onDragOver);
            drawer.removeEventListener("drop", onDrop);
        }
    };
}

/**
 * A file dropped on the import dialog fills its text box instead of opening a second preview. A
 * dialog that reads archives takes a .zip through OnFileBytesAsync; others get its text and say
 * it is not settings.
 */
export function watchDialogDrop(dialog, dotNetReference, readsArchives = false) {
    const onDragOver = event => {
        if (!isFileDrag(event)) return;
        event.preventDefault();
        event.dataTransfer.dropEffect = "copy";
    };
    const onDrop = event => {
        if (!isFileDrag(event)) return;
        event.preventDefault();
        event.stopPropagation();
        const file = event.dataTransfer.files?.[0];
        if (!file) return;
        if (readsArchives) sendFile(file, dotNetReference, "OnFileTextAsync", "OnFileBytesAsync");
        else void file.text().then(text => dotNetReference.invokeMethodAsync("OnFileTextAsync", text, file.name));
    };
    dialog.addEventListener("dragover", onDragOver);
    dialog.addEventListener("drop", onDrop);
    return {
        dispose: () => {
            dialog.removeEventListener("dragover", onDragOver);
            dialog.removeEventListener("drop", onDrop);
        }
    };
}
