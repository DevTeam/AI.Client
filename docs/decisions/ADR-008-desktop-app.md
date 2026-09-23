# ADR-008: Desktop app on Avalonia with the system web view

## Status

Accepted, 2026-09-23.

## Context

AI.Client ships as a standalone Host and must also ship as one desktop application for Windows, macOS and Linux. The UI is the existing Blazor WebAssembly app, and the Host-Web contract (HTTP + SSE over `AI.Client.Contracts`) must stay the only way the UI talks to the server. The main criterion is reliability.

## Decision

- `AI.Client.Desktop` is an Avalonia 12 app whose window holds one `NativeWebView` (`Avalonia.Controls.WebView`, MIT). It uses the system engine — WebView2, WKWebView, WPE WebKit/WebKitGTK — and bundles no browser.
- The desktop process runs the same server in-process (`AI.Client.Server`, same composition as the Host) on `127.0.0.1` with a port the OS picks, and the server also serves the UI. The UI and the API share one origin: no CORS, no custom scheme, identical on all three systems. Served that way, the server answers `/appsettings*.json` with its own origin, so the UI always calls the server that served it.
- No JavaScript bridge. Anything desktop-specific becomes an endpoint in the contract.
- The server listens on loopback only; no per-launch token (same exposure as the standalone Host).
- One server per data directory, enforced by an unshared `<data>/.lock` that the OS releases on any exit.
- Credentials on macOS/Linux: AES-256-GCM under a key in the Keychain / Secret Service, with a sticky fallback to an owner-only key file when no keyring works. Windows keeps DPAPI.
- The web view keeps its profile in `<data>/webview`; links to other sites open in the user's browser.

## Rejected

- Opening `index.html` from disk: `file://` has a null origin, the engines refuse `fetch` from it (Blazor loads its assemblies that way), and the API would need CORS for `null`.
- A custom scheme or virtual host per engine: works, but differently in each engine, and still needs CORS between UI and API origins.
- CefGlue/Chromium-based controls: 150–200 MB extra per build.
- A per-launch token cookie: not needed for a loopback-only server.

## Consequences

- The target machine needs the system web view: WebView2 Runtime on Windows (present on current Windows 10/11), WebKitGTK (`libwebkit2gtk-4.1`) or WPE WebKit on Linux. The window reports a missing engine instead of staying blank.
- A self-contained publish carries the .NET runtime twice (the app and the built-in MCP server process).
