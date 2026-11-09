# Desktop app

`AI.Desktop` is the Blazor UI in a native window. It uses the separately installed Host when available; otherwise the server runs inside the Desktop process. Decision and alternatives: [ADR-008](decisions/ADR-008-desktop-app.md).

## Run and publish

```powershell
dotnet run --project src/AI.Desktop -- --help
dotnet run --project build -- publish-desktop --runtime win-x64
dotnet run --project build -- publish-desktop --runtime osx-arm64
dotnet run --project build -- publish-desktop --runtime linux-x64
```

The publish is self-contained (no .NET needed on the target) and lands in `artifacts/desktop/<runtime>`. Options: `--data-dir`, `--no-browse` (shared with the standalone Host), `--dev-tools` (the web view's developer tools) and `--new-window` (one more window, see below).

For source debugging in Rider, use the shared **AI Desktop (local data)** run configuration. It keeps data in `artifacts/rider-desktop`, starts the server built from this checkout, and enables WebView developer tools. With the default data directory, Desktop uses a running compatible Host, or starts its own server when Host is stopped.

## Requirements on the target machine

| System | Needs |
|---|---|
| Windows 10/11 | Microsoft Edge WebView2 Runtime (usually preinstalled) |
| macOS | nothing extra (WKWebView) |
| Linux | WebKitGTK (`libwebkit2gtk-4.1-0`) or WPE WebKit; a Secret Service (GNOME Keyring, KWallet) and `secret-tool` for keyring-protected credentials, otherwise an owner-only key file in the data directory is used |

## How it starts

1. The command line is parsed on the main thread (`ICommandLineApplication.Run`): macOS requires the UI on it, WebView2 an STA thread.
2. One process per data directory (`ISingleInstance`). The first launch holds `<data>/.desktop.lock` and listens on a channel named after the directory: a named pipe on Windows, a Unix domain socket on macOS and Linux, both restricted to the current user. A later launch on the same directory connects to that channel instead and exits with code 0 before it looks for a Host or starts a server. It asks the running process to bring its most recently used window to the front (shown again, restored if minimized, activated), or, with `--new-window`, to open one more window. On Windows it first grants the running process the foreground (`AllowSetForegroundWindow`), which Windows would otherwise refuse it. If the running window is already closing, the launch waits for it to exit and starts normally. If the holder does not answer within 5 s (it hangs, or it is an older build without the channel), the launch starts as before, and the data-directory lock below still keeps the two apart. Another `--data-dir` is a separate instance.
3. The server starts on `http://127.0.0.1:0` and serves the UI; its actual address goes to the window. If it cannot start — for example another AI process uses the data directory — the window shows why.
4. The window navigates its `NativeWebView` to that address. The engine keeps its profile in `<data>/webview`. Navigation to other sites and new-window requests open in the user's browser; only `http`, `https` and `mailto` links leave the app.
5. If the engine never comes up (no WebView2 / WebKitGTK), the window says what to install after 20 s. A failed page load offers Retry.
6. The server's port, and with it the page's origin and its `localStorage`, changes on every start. The page's settings are kept in `<data>/client-settings.json`, and the rest of what it remembers — every `ai-client.*` entry but the settings, the published app's Host link and the guide's cross-tab lease — in `<data>/ui-state.json` (`JsonUiStateStore`, at most 4 MB). `theme.js` mirrors each write of such an entry to the window 400 ms later, and at once when the page is hidden or goes away (`ui-state-save`); the window hands both files back before Blazor starts (`restoreClientSettings`). On the first start without the file, whatever the origin holds becomes the first copy.
7. Closing the window stops the server (the SSE stream ends on shutdown, so exit is immediate) and releases the data directory. Ctrl+C, SIGTERM and SIGQUIT close the window the same way; the embedded server ignores signals itself (`ServerOptions.StopOnProcessSignals = false`), so it never stops under an open window.

## Windows

One process can show several windows over the same server: Ctrl+Shift+N (Cmd+Shift+N on macOS) in a window, or a launch with `--new-window`. The page sends `new-window` over the bridge (`js/desktopWindows.js`); in an ordinary browser the key is left alone. A new window opens 32 px down and right of the one it came from, at its normal size, maximized if that one is, and over it when the shift would leave every screen. It opens at the workspace location saved last.

The windows share the process: the server, the web view profile and its `localStorage`, the update manager (an install closes every window), and the files in the data directory. Each window keeps its own taskbar badge. Whichever window closes last saves the placement the next start restores. The process ends when its last window closes.

## Verified

- Windows 10 (19045): UI loads from the in-process server, API and SSE work, closing exits in ~0.2 s and releases the lock. A second and a third launch on the same data directory exit with code 0 in about 1 s, and a minimized first window is restored. `--new-window` and Ctrl+Shift+N each open a cascaded window that loads the app from the same server; a plain launch then opens none; closing the windows one by one ends the process with the last.
- Ubuntu 24.04 (WSLg, WebKitGTK 2.50): the published `linux-x64` app loads the UI, keeps the web view profile in `<data>/webview`, exits in ~0.1 s on SIGTERM and releases the lock; credential key fallback and key file permissions.
- Publish for `win-x64`, `linux-x64`, `osx-arm64` from Windows.
- Not yet run: anything on macOS (window, Keychain, the single-instance channel), a real Linux Secret Service, the single-instance channel on Linux.
