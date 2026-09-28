window.aiHostBridge = {
    consumeReturn() {
        const fragment = new URLSearchParams(location.hash.replace(/^#/, ""));
        const token = fragment.get("host_token");
        const state = fragment.get("host_state");
        if (!token && !state) return "none";
        const pending = sessionStorage.getItem("ai-client.host-state.v1");
        sessionStorage.removeItem("ai-client.host-state.v1");
        history.replaceState(null, "", location.pathname + location.search);
        if (pending && state === pending && /^[0-9A-F]{64}$/.test(token || "")) {
            localStorage.setItem("ai-client.host-token.v1", token);
            return "connected";
        }
        return "invalid";
    },
    connect() {
        const bytes = crypto.getRandomValues(new Uint8Array(16));
        const state = Array.from(bytes, b => b.toString(16).padStart(2, "0")).join("").toUpperCase();
        sessionStorage.setItem("ai-client.host-state.v1", state);
        location.assign(`http://127.0.0.1:52173/connect?state=${state}`);
    },
    async platform() {
        const platform = navigator.userAgentData?.platform || navigator.platform || "";
        let architecture = "";
        try {
            architecture = (await navigator.userAgentData?.getHighEntropyValues(["architecture"]))?.architecture || "";
        } catch { /* The selector still lets the user choose when browser detection is unavailable. */ }
        const arm = /arm/i.test(architecture);
        if (/win/i.test(platform)) return arm ? "win-arm64" : "win-x64";
        if (/mac/i.test(platform)) return /x86/i.test(architecture) ? "osx-x64" : "osx-arm64";
        return arm ? "linux-arm64" : "linux-x64";
    },
    token() { return localStorage.getItem("ai-client.host-token.v1"); },
    disconnect() {
        localStorage.removeItem("ai-client.host-token.v1");
        location.reload();
    }
};
