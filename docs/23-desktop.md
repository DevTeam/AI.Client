# Desktop app

`AI.Desktop` is the Blazor UI in a native window. It uses the separately installed Host when available; otherwise the server runs inside the Desktop process. Decision and alternatives: [ADR-008](decisions/ADR-008-desktop-app.md).

## Run and publish

```powershell
dotnet run --project src/AI.Desktop -- --help
dotnet run --project build -- publish-desktop --runtime win-x64
dotnet run --project build -- publish-desktop --runtime osx-arm64
dotnet run --project build -- publish-desktop --runtime linux-x64
```

The publish is self-contained (no .NET needed on the target) and lands in `artifacts/desktop/<runtime>`. Options: `--data-dir`, `--no-browse` (shared with the standalone Host) and `--dev-tools` (the web view's developer tools).

For source debugging in Rider, use the shared **AI Desktop (local data)** run configuration. It keeps data in `artifacts/rider-desktop`, starts the server built from this checkout, and enables WebView developer tools. With the default data directory, Desktop uses a running compatible Host, or starts its own server when Host is stopped.

## Requirements on the target machine

| System | Needs |
|---|---|
| Windows 10/11 | Microsoft Edge WebView2 Runtime (usually preinstalled) |
| macOS | nothing extra (WKWebView) |
| Linux | WebKitGTK (`libwebkit2gtk-4.1-0`) or WPE WebKit; a Secret Service (GNOME Keyring, KWallet) and `secret-tool` for keyring-protected credentials, otherwise an owner-only key file in the data directory is used |

## How it starts

1. The command line is parsed on the main thread (`ICommandLineApplication.Run`): macOS requires the UI on it, WebView2 an STA thread.
2. The server starts on `http://127.0.0.1:0` and serves the UI; its actual address goes to the window. If it cannot start — for example another AI process uses the data directory — the window shows why.
3. The window navigates its `NativeWebView` to that address. The engine keeps its profile in `<data>/webview`. Navigation to other sites and new-window requests open in the user's browser; only `http`, `https` and `mailto` links leave the app.
4. If the engine never comes up (no WebView2 / WebKitGTK), the window says what to install after 20 s. A failed page load offers Retry.
5. Closing the window stops the server (the SSE stream ends on shutdown, so exit is immediate) and releases the data directory. Ctrl+C, SIGTERM and SIGQUIT close the window the same way; the embedded server ignores signals itself (`ServerOptions.StopOnProcessSignals = false`), so it never stops under an open window.

## Verified

- Windows 10 (19045): UI loads from the in-process server, API and SSE work, a second instance on the same data directory shows the lock message, closing exits in ~0.2 s and releases the lock.
- Ubuntu 24.04 (WSLg, WebKitGTK 2.50): the published `linux-x64` app loads the UI, keeps the web view profile in `<data>/webview`, exits in ~0.1 s on SIGTERM and releases the lock; credential key fallback and key file permissions.
- Publish for `win-x64`, `linux-x64`, `osx-arm64` from Windows.
- Not yet run: anything on macOS (window, Keychain), a real Linux Secret Service.
