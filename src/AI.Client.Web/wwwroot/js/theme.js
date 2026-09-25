// Sets <html data-theme="light|dark"> from the saved preference. Loaded as a plain script in
// <head>, ahead of the stylesheet, so the attribute is in place before the first paint and there
// is no flash of the wrong theme while Blazor boots. The entry is the one ClientSettingsService
// writes: "ai-client.settings" = {"theme":"system|light|dark"}. data-theme-preference keeps the
// choice itself, for styles that care whether the theme was picked or inherited.
(function () {
    const storageKey = "ai-client.settings";
    const systemLight = matchMedia("(prefers-color-scheme: light)");
    let preference = "system";

    // The browser chrome colour cannot come from CSS: this script runs before the stylesheet,
    // so these mirror --color-bg of each theme in app.css.
    const chromeColors = { dark: "#171717", light: "#ffffff" };

    function render() {
        const root = document.documentElement;
        const theme = preference === "system" ? (systemLight.matches ? "light" : "dark") : preference;
        root.dataset.themePreference = preference;
        if (root.dataset.theme === theme) return;
        root.dataset.theme = theme;
        document.querySelector('meta[name="theme-color"]')?.setAttribute("content", chromeColors[theme]);
        // Mermaid (index.html) re-reads its palette from the tokens on this event.
        document.dispatchEvent(new CustomEvent("ai-client-theme-change", { detail: { theme } }));
    }

    function apply(value) {
        preference = value === "light" || value === "dark" ? value : "system";
        render();
    }

    let saved;
    try { saved = JSON.parse(localStorage.getItem(storageKey) || "null")?.theme; } catch { saved = undefined; }
    apply(saved);
    systemLight.addEventListener("change", () => { if (preference === "system") render(); });

    globalThis.aiClientTheme = { apply };
})();
