// Measures the real width of an element's vertical scrollbar and writes the
// value to a CSS custom property. Lets the absolutely-positioned chat header
// stop before the scrollbar on platforms where the browser draws a
// non-overlay one (Windows, Linux), while collapsing to 0 on macOS where
// scrollbars float over the content.
//
// `scrollbar-gutter: stable` (declared in app.css on .workspace-messages)
// reserves the gutter whether or not content overflows, so the measured width
// stays stable as the transcript grows or shrinks mid-chat.
//
// The variable is written on a *separate* element from the one we measure:
// CSS custom properties inherit downwards, so the consumer (.workspace-header)
// needs to be a descendant of the element we set the variable on. Here the
// scrollbar container (.workspace-messages) is a sibling of the header inside
// .workspace-conversation, so the variable goes on .workspace-conversation and
// both children inherit it.

const RESIZE_OBSERVER_SUPPORTED = typeof ResizeObserver !== "undefined";

function measureScrollbarWidth(element) {
    if (!element) return 0;
    // `offsetWidth` includes the scrollbar; `clientWidth` does not. The
    // difference is the gutter width — 0 on macOS overlay scrollbars, the
    // theme width (typically ~17px on Windows default) on classic ones.
    return Math.max(0, element.offsetWidth - element.clientWidth);
}

function resolve(value) {
    return typeof value === "string" ? document.querySelector(value) : value;
}

export function attach(measureSelector, varTargetSelector, cssVarName) {
    const measure = resolve(measureSelector);
    const varTarget = resolve(varTargetSelector);
    if (!measure || !varTarget) {
        return { dispose: () => {} };
    }

    let rafHandle = 0;

    const update = () => {
        rafHandle = 0;
        const width = measureScrollbarWidth(measure);
        varTarget.style.setProperty(cssVarName, `${width}px`);
    };

    const scheduleUpdate = () => {
        if (rafHandle !== 0) return;
        rafHandle = window.requestAnimationFrame(update);
    };

    // Initial measurement, then on every layout change that could shift the
    // scrollbar: window resize, sidebar resize, message arrival, etc.
    update();

    let observer = null;
    if (RESIZE_OBSERVER_SUPPORTED) {
        observer = new ResizeObserver(scheduleUpdate);
        observer.observe(measure);
    }
    window.addEventListener("resize", scheduleUpdate);

    return {
        dispose: () => {
            if (rafHandle !== 0) {
                window.cancelAnimationFrame(rafHandle);
                rafHandle = 0;
            }
            if (observer) observer.disconnect();
            window.removeEventListener("resize", scheduleUpdate);
            varTarget.style.removeProperty(cssVarName);
        }
    };
}
