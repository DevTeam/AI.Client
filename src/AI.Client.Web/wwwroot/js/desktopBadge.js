// This module also runs in an ordinary browser, where Avalonia's bridge is absent.
export function publishUnreadCount(count) {
    if (typeof globalThis.invokeCSharpAction === "function")
        globalThis.invokeCSharpAction(JSON.stringify({ type: "unread-count", count }));
}
