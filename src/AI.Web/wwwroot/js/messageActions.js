(() => {
    // Copy gives no other sign that it worked: the icon turns into a check for a moment.
    const copiedFor = 1200;
    document.addEventListener("click", event => {
        const button = event.target instanceof Element ? event.target.closest(".message-copy-action") : null;
        if (!button) return;
        button.classList.add("is-copied");
        clearTimeout(button.copiedTimer);
        button.copiedTimer = setTimeout(() => button.classList.remove("is-copied"), copiedFor);
    });

})();
