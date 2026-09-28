// Sets <html data-theme="light|dark"> from the saved preference. Loaded as a plain script in
// <head>, ahead of the stylesheet, so the attribute is in place before the first paint and there
// is no flash of the wrong theme while Blazor boots. The entry is the one ClientSettingsService
// writes: "ai-client.settings" = {"theme":"system|light|dark","accent":"blue|teal|..."}.
// data-theme-preference keeps the choice itself, for styles that care whether the theme was
// picked or inherited; data-accent picks the accent palette in app.css.
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

    // Inside the desktop app the titlebar is native and follows the page through Avalonia's
    // bridge (MainWindow.OnWebMessageReceived). The preference, not the resolved theme, is sent:
    // "system" lets the host follow the OS itself. An ordinary browser has no bridge.
    let toldHost;
    function tellHost() {
        if (toldHost === preference || typeof globalThis.invokeCSharpAction !== "function") return;
        globalThis.invokeCSharpAction(JSON.stringify({ type: "theme", preference }));
        toldHost = preference;
    }

    function apply(value) {
        preference = value === "light" || value === "dark" ? value : "system";
        render();
        tellHost();
    }

    // Must match AccentColor; anything else (an older or newer build's value) falls back to blue.
    const accents = ["blue", "teal", "green", "amber", "orange", "pink", "purple", "indigo"];
    function applyAccent(value) {
        document.documentElement.dataset.accent = accents.includes(value) ? value : "blue";
    }

    function applyCornerRoundness(value) {
        const percent = Number(value);
        const clamped = Number.isFinite(percent) ? Math.max(0, Math.min(200, percent)) : 100;
        document.documentElement.style.setProperty("--corner-scale", String(clamped / 100));
    }

    let saved;
    try { saved = JSON.parse(localStorage.getItem(storageKey) || "null"); } catch { saved = null; }
    applyAccent(saved?.accent);
    applyCornerRoundness(saved?.cornerRoundnessPercent);
    apply(saved?.theme);
    systemLight.addEventListener("change", () => { if (preference === "system") render(); });
    // The bridge may not be injected yet while <head> runs; catch up once the document is parsed.
    document.addEventListener("DOMContentLoaded", tellHost);

    globalThis.aiClientTheme = { apply, applyAccent, applyCornerRoundness };
})();
