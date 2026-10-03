// Inline guide text has no transcript handler. Keep navigation inside the app, including
// modifier and middle clicks, and route shortcuts through the same validated Blazor callback.
export function attach(container, dotnet) {
    const onClick = event => {
        if (event.defaultPrevented || event.type === 'auxclick' && event.button !== 1) return;
        const anchor = event.target instanceof Element ? event.target.closest('a[href]') : null;
        if (!anchor || !container.contains(anchor)) return;
        event.preventDefault();
        event.stopPropagation();
        const href = (anchor.getAttribute('href') ?? '').trim();
        if (/^aiclient:/i.test(href)) {
            void dotnet.invokeMethodAsync('OnAppNavigationLinkClicked', href);
            return;
        }
        if (/^(?:https?:|mailto:|\/\/)/i.test(href)) {
            try {
                const url = new URL(href, location.href);
                if (!['http:', 'https:', 'mailto:'].includes(url.protocol)) return;
                if (typeof globalThis.invokeCSharpAction === 'function')
                    globalThis.invokeCSharpAction(JSON.stringify({ type: 'open-external-link', url: url.href }));
                else window.open(url.href, '_blank', 'noopener,noreferrer');
            } catch { /* Malformed destinations remain inert. */ }
        }
    };
    container.addEventListener('click', onClick, true);
    container.addEventListener('auxclick', onClick, true);
    return {
        dispose() {
            container.removeEventListener('click', onClick, true);
            container.removeEventListener('auxclick', onClick, true);
        }
    };
}
