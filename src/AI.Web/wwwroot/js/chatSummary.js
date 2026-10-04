// Keep a round summary under the pointer when its in-flow details grow upward.
export function captureHeader(header) {
    return header.closest(".workspace-messages") ? header.getBoundingClientRect().top : null;
}

export function restoreHeader(header, top) {
    const scroller = header.closest(".workspace-messages");
    if (scroller) scroller.scrollTop += header.getBoundingClientRect().top - top;
}
