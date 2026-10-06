// Sets <html data-theme="dark|darkblue|gray|light|lightgray"> from the saved preference. Loaded as a plain
// script in <head>, ahead of the stylesheet, so browser preferences apply before the first paint.
// The entry is the one ClientSettingsService
// writes: "ai-client.settings" = {"theme":"system|light|dark|darkBlue","accent":"blue|teal|...",
// "cornerRoundnessPercent":100,...}. Desktop restores the entry from its profile before Blazor starts.
// data-theme-preference keeps the choice itself, for styles that care whether the theme was
// picked or inherited; data-accent picks the accent palette in app.css.
(function () {
    const storageKey = "ai-client.settings";
    const systemLight = matchMedia("(prefers-color-scheme: light)");
    let preference = "system";

    // The browser chrome colour cannot come from CSS: this script runs before the stylesheet,
    // so these mirror --color-bg of each theme in app.css.
    const chromeColors = { dark: "#171717", light: "#ffffff", darkblue: "#101720", gray: "#212121", lightgray: "#f2f2f2" };

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

    // Must match ThemePreference, lowercased by ThemeService. The stored JSON spells the value in
    // camelCase ("darkBlue"), so the value is folded before the lookup. "system" follows the OS;
    // anything else (an older or newer build's value) leaves that to the OS as well.
    const preferences = ["system", "light", "dark", "darkblue", "gray", "lightgray"];

    function apply(value, notifyHost = true) {
        const normalized = typeof value === "string" ? value.toLowerCase() : "";
        preference = preferences.includes(normalized) ? normalized : "system";
        render();
        if (notifyHost) tellHost();
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

    function applyOtherSounds(value) {
        document.documentElement.dataset.otherSounds = String(value !== false);
    }

    let ready;
    globalThis.aiClientSettingsReady = new Promise(resolve => { ready = resolve; });

    function saveClientSettings(json) {
        localStorage.setItem(storageKey, json);
        applyOtherSounds(JSON.parse(json)?.otherSoundsEnabled);
        if (typeof globalThis.invokeCSharpAction === "function") {
            globalThis.invokeCSharpAction(JSON.stringify({ type: "client-settings-save", settings: json }));
        }
    }

    function restoreClientSettings(json) {
        // On first use there is no desktop file yet: keep any preferences already on this origin.
        if (json !== null) localStorage.setItem(storageKey, json);
        else if (localStorage.getItem(storageKey)) saveClientSettings(localStorage.getItem(storageKey));
        let restored;
        try { restored = JSON.parse(localStorage.getItem(storageKey) || "null"); } catch { restored = null; }
        applyAccent(restored?.accent);
        applyCornerRoundness(restored?.cornerRoundnessPercent);
        applyOtherSounds(restored?.otherSoundsEnabled);
        apply(restored?.theme);
        ready();
    }

    let saved;
    try { saved = JSON.parse(localStorage.getItem(storageKey) || "null"); } catch { saved = null; }
    applyAccent(saved?.accent);
    applyCornerRoundness(saved?.cornerRoundnessPercent);
    applyOtherSounds(saved?.otherSoundsEnabled);
    apply(saved?.theme, false);
    systemLight.addEventListener("change", () => { if (preference === "system") render(); });
    // The bridge may not be injected yet while <head> runs. Wait for it before Blazor reads the
    // settings; Desktop's server gets a new port on each start, so its localStorage origin changes.
    document.addEventListener("DOMContentLoaded", () => {
        if (typeof globalThis.invokeCSharpAction === "function") {
            globalThis.invokeCSharpAction(JSON.stringify({ type: "client-settings-request" }));
            setTimeout(ready, 2000);
        } else {
            tellHost();
            ready();
        }
    });

    globalThis.aiClientTheme = { apply, applyAccent, applyCornerRoundness, saveClientSettings, restoreClientSettings };
})();
