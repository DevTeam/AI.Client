window.aiHostBridge = (() => {
    const tokenKey = "ai-client.host-token.v1";
    const stateKey = "ai-client.host-state.v1";
    // Set for this tab once the Web app has sent the user to the Host's confirmation page, or
    // after they disconnected: from then on the page waits for a click instead of redirecting again.
    const manualKey = "ai-client.host-manual.v1";
    const session = {
        get(key) { try { return sessionStorage.getItem(key); } catch { return null; } },
        set(key, value) { try { sessionStorage.setItem(key, value); } catch { /* A private window still connects, it only asks again. */ } },
        remove(key) { try { sessionStorage.removeItem(key); } catch { /* Nothing to forget. */ } }
    };
    return {
        // What the address fragment brought back: "connected", "invalid", "cancelled",
        // "pair:<code>" from `AI.Host open`, or "none".
        consumeReturn() {
            const fragment = new URLSearchParams(location.hash.replace(/^#/, ""));
            const token = fragment.get("host_token");
            const state = fragment.get("host_state");
            const pair = fragment.get("host_pair");
            const cancelled = fragment.has("host_cancelled");
            if (!token && !state && !pair && !cancelled) return "none";
            history.replaceState(null, "", location.pathname + location.search);
            if (pair) return /^[0-9A-Fa-f]{32}$/.test(pair) ? `pair:${pair}` : "invalid";
            if (cancelled) {
                session.set(manualKey, "1");
                return "cancelled";
            }
            const pending = session.get(stateKey);
            session.remove(stateKey);
            if (pending && state === pending && /^[0-9A-F]{64}$/.test(token || "")) {
                localStorage.setItem(tokenKey, token);
                return "connected";
            }
            return "invalid";
        },
        connect(hostAddress) {
            const bytes = crypto.getRandomValues(new Uint8Array(16));
            const state = Array.from(bytes, b => b.toString(16).padStart(2, "0")).join("").toUpperCase();
            session.set(stateKey, state);
            session.set(manualKey, "1");
            location.assign(`${hostAddress.replace(/\/+$/, "")}/connect?state=${state}`);
        },
        // True when this tab may go to the confirmation page without a click.
        mayConnectAutomatically() { return session.get(manualKey) !== "1"; },
        connected() { session.remove(manualKey); },
        async platform() {
            const platform = navigator.userAgentData?.platform || navigator.platform || "";
            let architecture = "";
            try {
                architecture = (await navigator.userAgentData?.getHighEntropyValues(["architecture"]))?.architecture || "";
            } catch { /* The platform list still lets the user choose when browser detection is unavailable. */ }
            const arm = /arm/i.test(architecture);
            if (/win/i.test(platform)) return arm ? "win-arm64" : "win-x64";
            if (/mac/i.test(platform)) return /x86/i.test(architecture) ? "osx-x64" : "osx-arm64";
            return arm ? "linux-arm64" : "linux-x64";
        },
        // "granted", "denied" or "prompt" where the browser has Local Network Access, else "unknown".
        async localNetworkAccess() {
            try {
                return (await navigator.permissions.query({ name: "local-network-access" })).state;
            } catch {
                return "unknown";
            }
        },
        token() { return localStorage.getItem(tokenKey); },
        store(token) { if (/^[0-9A-F]{64}$/.test(token || "")) localStorage.setItem(tokenKey, token); },
        forget() { localStorage.removeItem(tokenKey); },
        disconnect() {
            localStorage.removeItem(tokenKey);
            session.set(manualKey, "1");
            location.reload();
        }
    };
})();
