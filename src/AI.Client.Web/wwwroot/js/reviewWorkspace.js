(() => {
    const storageKey = "review-tree-width";

    window.reviewWorkspace = {
        initialize(id) {
            const body = document.getElementById(id);
            if (!body || body.dataset.resizeReady) return;
            const handle = body.querySelector(".review-tree-resizer");
            if (!handle) return;
            body.dataset.resizeReady = "true";

            const apply = width => {
                const max = Math.max(180, Math.min(600, body.clientWidth - 320));
                const value = Math.round(Math.max(180, Math.min(max, width)));
                body.style.setProperty("--review-tree-width", `${value}px`);
                handle.setAttribute("aria-valuenow", value);
                return value;
            };

            const saved = Number(localStorage.getItem(storageKey));
            if (Number.isFinite(saved) && saved > 0) apply(saved);

            handle.addEventListener("pointerdown", event => {
                if (event.button !== 0) return;
                event.preventDefault();
                handle.setPointerCapture(event.pointerId);
                body.classList.add("is-resizing");
            });
            handle.addEventListener("pointermove", event => {
                if (!handle.hasPointerCapture(event.pointerId)) return;
                apply(event.clientX - body.getBoundingClientRect().left);
            });
            const finish = event => {
                if (!handle.hasPointerCapture(event.pointerId)) return;
                handle.releasePointerCapture(event.pointerId);
                body.classList.remove("is-resizing");
                localStorage.setItem(storageKey, handle.getAttribute("aria-valuenow"));
            };
            handle.addEventListener("pointerup", finish);
            handle.addEventListener("pointercancel", finish);
            handle.addEventListener("keydown", event => {
                if (event.key !== "ArrowLeft" && event.key !== "ArrowRight") return;
                event.preventDefault();
                const current = Number(handle.getAttribute("aria-valuenow")) || 352;
                localStorage.setItem(storageKey, apply(current + (event.key === "ArrowRight" ? 16 : -16)));
            });
        }
    };
})();
