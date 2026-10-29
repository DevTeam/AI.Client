// Sets <html data-theme="dark|darkblue|gray|light|lightgray|custom"> from the saved preference, Loaded as a plain
// script in <head>, ahead of the stylesheet, so browser preferences apply before the first paint.
// The entry is the one ClientSettingsService
// writes: "ai-client.settings" = {"theme":"system|light|dark|darkBlue","accent":"blue|teal|...",
// "cornerRoundnessPercent":100,"activeCustomThemeId":"...","customThemes":[...],...}. Desktop restores
// the entry from its profile before Blazor starts, together with a copy of the page's other
// "ai-client.*" state (see restoreClientSettings).
// data-theme-preference keeps the choice itself, for styles that care whether the theme was
// picked or inherited; data-accent picks the accent palette in app.css.
// data-theme-family="dark|light" says which way a theme ramps away from its background, so the
// stylesheet's light-family rules can name the family instead of listing the preset names, and a
// user's own light theme gets them too. A custom theme's colours are already resolved when it is
// saved and are written as inline properties here, before the stylesheet is read: the seeded engine
// lives on the .NET side (AI.Contracts/Themes/ThemePalette.cs), and this script only applies what it
// produced, so a custom theme cannot flash the preset underneath it.
(function () {
    const storageKey = "ai-client.settings";
    const systemLight = matchMedia("(prefers-color-scheme: light)");
    let preference = "system";
    // The active custom theme, or null. Its palette was resolved when it was saved.
    let custom = null;

    // The browser chrome colour cannot come from CSS: this script runs before the stylesheet,
    // so these mirror --color-bg of each theme in app.css. A custom theme carries its own.
    const chromeColors = { dark: "#171717", light: "#ffffff", darkblue: "#101720", gray: "#212121", lightgray: "#dedede" };

    function resolvedTheme() {
        if (custom) return "custom";
        return preference === "system" ? (systemLight.matches ? "light" : "dark") : preference;
    }

    // Which way a theme ramps away from its background. A preset's family is fixed here; a custom
    // theme carries the one its seeds resolved to.
    function familyOf(theme) {
        if (theme === "custom") return custom && custom.family === "light" ? "light" : "dark";
        return theme === "light" || theme === "lightgray" ? "light" : "dark";
    }

    function chromeColor(theme) {
        if (theme !== "custom") return chromeColors[theme];
        const tokens = (custom && custom.tokens) || {};
        return tokens["--color-bg"] || chromeColors[familyOf(theme)];
    }

    // The palette a custom theme carries, keyed by the CSS property names app.css uses. Only the
    // roles a theme controls are applied: the accent, the brand and what a status or diff means keep
    // coming from the stylesheet, so a custom theme cannot repaint them. This bounds the written
    // properties even when a stored theme was hand-edited or written by another build.
    const reservedRoles = new Set(["unread", "brand", "generating", "user-bubble", "user-bubble-border",
        "user-bubble-inset", "diff-added-bg", "diff-removed-bg", "highlight-bg", "highlight-fg",
        "accent", "accent-strong", "accent-text", "accent-bg", "accent-bg-hover", "accent-bg-active",
        "accent-fill", "on-accent", "overlay-faint", "overlay-hover", "overlay-active", "overlay-sunken",
        "border-translucent", "context-instructions", "context-tools", "context-history", "context-draft",
        "context-reserve", "context-overhead"]);

    function customProperties() {
        if (!custom || !custom.tokens) return {};
        const properties = {};
        for (const [name, value] of Object.entries(custom.tokens)) {
            if (!name.startsWith("--color-") || typeof value !== "string" || !value) continue;
            if (reservedRoles.has(name.slice("--color-".length))) continue;
            properties[name] = value;
        }
        return properties;
    }

    let appliedCustomProperties = [];
    function applyCustomProperties(theme) {
        const root = document.documentElement;
        const wanted = theme === "custom" ? customProperties() : {};
        for (const name of appliedCustomProperties) {
            if (!(name in wanted)) root.style.removeProperty(name);
        }
        for (const [name, value] of Object.entries(wanted)) root.style.setProperty(name, value);
        appliedCustomProperties = Object.keys(wanted);
    }

    function render() {
        const root = document.documentElement;
        const theme = resolvedTheme();
        const family = familyOf(theme);
        root.dataset.themePreference = preference;
        // The palette is rewritten even when the theme's name has not changed: two custom themes share
        // the name "custom", and a person editing one sees its colours move while the name stands still.
        applyCustomProperties(theme);
        const unchanged = root.dataset.theme === theme && root.dataset.themeFamily === family;
        root.dataset.theme = theme;
        root.dataset.themeFamily = family;
        document.querySelector('meta[name="theme-color"]')?.setAttribute("content", chromeColor(theme));
        if (unchanged) return;
        // Mermaid (index.html) re-reads its palette from the tokens on this event.
        document.dispatchEvent(new CustomEvent("ai-client-theme-change", { detail: { theme, family } }));
    }

    // Inside the desktop app the window and its system titlebar follow the page through Avalonia's
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

    // The theme the studio or a restored entry wants shown: null for a preset, or the saved theme
    // itself. Its tokens are applied as they are, so the page never computes a palette in the browser.
    function applyCustomTheme(theme, notifyHost = true) {
        custom = theme && theme.tokens ? theme : null;
        render();
        if (notifyHost) tellHost();
    }

    // The entry names its active theme by id; the theme itself is one of the saved ones.
    function activeCustomTheme(settings) {
        const id = settings && settings.activeCustomThemeId;
        const themes = (settings && settings.customThemes) || [];
        if (!id || !Array.isArray(themes)) return null;
        return themes.find(theme => theme && theme.id === id) || null;
    }

    // The accent that goes with what paints the page: a saved theme keeps its own, the presets share
    // the entry's. A theme saved before themes kept an accent has none and follows the presets'.
    // ThemeService tells the page the same answer whenever it switches themes.
    function accentOf(settings) {
        return activeCustomTheme(settings)?.accent || settings?.accent;
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

    // Settings → Appearance → Animations; app.css and the scripts that move things read it.
    function applyAnimations(value) {
        document.documentElement.dataset.animations = String(value !== false);
    }

    let ready;
    globalThis.aiClientSettingsReady = new Promise(resolve => { ready = resolve; });

    function saveClientSettings(json) {
        localStorage.setItem(storageKey, json);
        const settings = JSON.parse(json);
        applyOtherSounds(settings?.otherSoundsEnabled);
        applyAnimations(settings?.animationsEnabled);
        if (typeof globalThis.invokeCSharpAction === "function") {
            globalThis.invokeCSharpAction(JSON.stringify({ type: "client-settings-save", settings: json }));
        }
    }

    // The page's other remembered state ("ai-client.*": sidebar layout, the project tree's folds,
    // drafts, ...) lives in localStorage too, which the desktop loses with the origin on every start.
    // The desktop keeps a copy: every write is mirrored to it, shortly after and when the page goes
    // away, and the copy is put back before Blazor starts. Settings have their own entry above; the
    // published app's Host link and the guide's cross-tab lease are not the desktop's to keep.
    const notMirrored = new Set([storageKey, "ai-client.host-token.v1", "ai-client.host-state.v1",
        "ai-client.host-manual.v1", "ai-client.guide-offer-lease"]);
    const isMirrored = key => typeof key === "string" && key.startsWith("ai-client.") && !notMirrored.has(key);
    let mirrorTimer = 0;
    let originalSetItem = null;

    function sendUiState() {
        clearTimeout(mirrorTimer);
        mirrorTimer = 0;
        const state = {};
        for (let index = 0; index < localStorage.length; index++) {
            const key = localStorage.key(index);
            if (isMirrored(key)) state[key] = localStorage.getItem(key);
        }
        globalThis.invokeCSharpAction(JSON.stringify({ type: "ui-state-save", state: JSON.stringify(state) }));
    }

    // Installed once, when the desktop answers; returns the unmirrored setItem for the restore.
    function mirrorUiState() {
        if (originalSetItem) return originalSetItem;
        const storage = Storage.prototype;
        const setItem = storage.setItem;
        const removeItem = storage.removeItem;
        const schedule = key => {
            if (!isMirrored(key)) return;
            clearTimeout(mirrorTimer);
            mirrorTimer = setTimeout(sendUiState, 400);
        };
        storage.setItem = function (key, value) { setItem.call(this, key, value); if (this === localStorage) schedule(key); };
        storage.removeItem = function (key) { removeItem.call(this, key); if (this === localStorage) schedule(key); };
        addEventListener("pagehide", () => { if (mirrorTimer) sendUiState(); });
        document.addEventListener("visibilitychange", () => { if (document.visibilityState === "hidden" && mirrorTimer) sendUiState(); });
        originalSetItem = setItem;
        return setItem;
    }

    function restoreClientSettings(json, uiState) {
        // On first use there is no desktop file yet: keep any preferences already on this origin.
        if (json !== null) localStorage.setItem(storageKey, json);
        else if (localStorage.getItem(storageKey)) saveClientSettings(localStorage.getItem(storageKey));
        const setItem = mirrorUiState();
        let entries = null;
        try { entries = uiState ? JSON.parse(uiState) : null; } catch { entries = null; }
        if (entries && typeof entries === "object") {
            for (const [key, value] of Object.entries(entries)) {
                if (isMirrored(key) && typeof value === "string") setItem.call(localStorage, key, value);
            }
        } else {
            // No copy yet: whatever this origin already holds becomes the first one.
            sendUiState();
        }
        let restored;
        try { restored = JSON.parse(localStorage.getItem(storageKey) || "null"); } catch { restored = null; }
        applyAccent(accentOf(restored));
        applyCornerRoundness(restored?.cornerRoundnessPercent);
        applyOtherSounds(restored?.otherSoundsEnabled);
        applyAnimations(restored?.animationsEnabled);
        applyCustomTheme(activeCustomTheme(restored), false);
        apply(restored?.theme, false);
        ready();
    }

    let saved;
    try { saved = JSON.parse(localStorage.getItem(storageKey) || "null"); } catch { saved = null; }
    applyAccent(accentOf(saved));
    applyCornerRoundness(saved?.cornerRoundnessPercent);
    applyOtherSounds(saved?.otherSoundsEnabled);
    applyAnimations(saved?.animationsEnabled);
    applyCustomTheme(activeCustomTheme(saved), false);
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

    globalThis.aiClientTheme = { apply, applyAccent, applyCornerRoundness, applyCustomTheme, saveClientSettings, restoreClientSettings };
})();
