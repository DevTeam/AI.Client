// Links to local files and directories in rendered messages. The model is asked to write them as
// file URIs; a bare Windows path is accepted too. Such a link cannot be followed from a web view,
// so a click does nothing, and a right-click hands the path to .NET, which offers to add it to the
// next message as a workspace reference.

// The local path a link names, or null for any other link.
const pathOf = anchor => {
    const href = anchor.getAttribute('href') ?? '';
    if (/^file:/i.test(href)) {
        try {
            const url = new URL(href);
            const path = decodeURIComponent(url.pathname);
            if (url.host) return `\\\\${url.host}${path.replace(/\//g, '\\')}`;
            // file:///C:/repo → C:\repo; file:///home/me stays a POSIX path.
            if (/^\/[A-Za-z]:/.test(path)) return path.slice(1).replace(/\//g, '\\');
            return path || null;
        } catch {
            return null;
        }
    }
    if (/^[A-Za-z]:(\\|\/|%5C)/i.test(href)) {
        try {
            return decodeURIComponent(href);
        } catch {
            return href;
        }
    }
    return null;
};

const linkIn = (container, event) => {
    const anchor = event.target instanceof Element ? event.target.closest('a[href]') : null;
    return anchor && container.contains(anchor) ? anchor : null;
};

// Marks file links once so they look different from web links and explain the right-click.
const markAll = container => {
    for (const anchor of container.querySelectorAll('.markdown-content a[href]:not([data-file-link])')) {
        const path = pathOf(anchor);
        anchor.dataset.fileLink = path ? 'true' : 'false';
        if (!path) continue;
        anchor.classList.add('file-link');
        anchor.title = `${path}\nRight-click to add it to the message`;
    }
};

export function attach(container, dotnet) {
    let scheduled = false;
    let disposed = false;
    const schedule = () => {
        if (scheduled || disposed) return;
        scheduled = true;
        requestAnimationFrame(() => {
            scheduled = false;
            if (!disposed) markAll(container);
        });
    };

    const onClick = event => {
        const anchor = linkIn(container, event);
        if (anchor && pathOf(anchor)) event.preventDefault();
    };
    const onContextMenu = event => {
        const anchor = linkIn(container, event);
        const path = anchor ? pathOf(anchor) : null;
        if (!path) return;
        event.preventDefault();
        dotnet.invokeMethodAsync('OnFileLinkContextMenu', path, event.clientX, event.clientY);
    };

    container.addEventListener('click', onClick);
    container.addEventListener('contextmenu', onContextMenu);
    const observer = new MutationObserver(schedule);
    observer.observe(container, { childList: true, subtree: true, characterData: true });
    schedule();
    return {
        dispose: () => {
            disposed = true;
            observer.disconnect();
            container.removeEventListener('click', onClick);
            container.removeEventListener('contextmenu', onContextMenu);
        }
    };
}
