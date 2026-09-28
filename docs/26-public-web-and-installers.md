# Public Web and installers

## User flow

`https://ai.dev-team.org/` serves the static Blazor WebAssembly client from GitHub Pages. It does not store projects or credentials. Chrome and Edge connect to a Host running on the same computer at `http://127.0.0.1:52173/`. The browser may ask for Local Network Access ([Chrome](https://developer.chrome.com/blog/local-network-access), [Edge](https://learn.microsoft.com/en-us/deployedge/ms-edge-local-network-access)). When no Host answers, the page offers one Host download for the detected platform (others under *Other platforms*) and a link to Desktop, then checks for the Host every two seconds and continues by itself once it runs.

The Host starts in the background after user login. The page never asks what it can find out: a browser with a valid grant opens the workspace at once, and a browser without one goes straight to the Host's local confirmation page, where the user grants the published origin access with one click. After Cancel, a failed return or Disconnect, the tab waits for **Connect to Host** instead of redirecting again. `AI.Host open` (the Windows installer's last step and its *AI Client in browser* Start menu entry, and the Linux launcher) needs no confirmation at all: it starts the Host if needed, asks it at `POST /api/bridge/launch` for a two-minute single-use code, and opens the Web app with `#host_pair=<code>`, which the page exchanges at `POST /api/bridge/pair` for a grant. The launch endpoint refuses any request carrying `Origin` or `Sec-Fetch-Site`, so only a program on this computer, not a web page, can get a code. The Host issues a random browser grant, stores only its hash in the local data directory, and requires it for public-origin API calls. **Settings → Host** in the Web app shows the Host's version and address and revokes the grant with **Disconnect**; the only permanent sign of the Host elsewhere is a status dot on **Settings**, green when the event stream is connected and amber while it reconnects. Desktop, when the Host reports it installed, is no longer offered. No .NET runtime installation is needed: all packages publish self-contained binaries.

Open the public Web app with **HTTPS**. The Host deliberately rejects `http://ai.dev-team.org`, even if the Host is running. On Windows, the installer registers a per-user scheduled task named `AI.Client.Host`; no Start menu window is needed. If it is stopped, run `Start-ScheduledTask -TaskName AI.Client.Host` in PowerShell. `http://127.0.0.1:52173/api/health` should then return the Host's status in the same computer's browser.

The Desktop app uses a running, compatible Host when it is available. If the installed Host is stopped, Desktop starts its embedded server with the same data directory. The data-directory lock prevents simultaneous writers; a background Host that starts later waits until Desktop closes. A running but incompatible Host is reported as an error. Local Host and Web development keep their existing behavior.

## Debugging the public Web app

Run **AI Host + Public Web** in Rider (`dotnet run --project build -- run-both --public-web`). It starts the Host with `--public-web --public-origin http://localhost:52175` and the Web dev server on `http://localhost:52175`. `appsettings.Development.json` turns the development build into the published app on that origin only (`PublicWebDevelopment`), talking to `http://127.0.0.1:52173/` with a browser grant; port 52174 keeps the ordinary development mode. `--public-origin` accepts HTTPS origins, and HTTP only on loopback. Loopback-to-loopback requests are not subject to Local Network Access, so that prompt appears only on the real site.

## Compatibility

The Web and Desktop clients check the Host's product name and `ApiVersion` before opening the workspace. The first public HTTP contract is version **1**. A client and Host with the same API version can work together even when their installer release numbers differ; new endpoints and fields within that version must remain backward compatible. Any breaking API change must increment `HostProtocol.ApiVersion` in the shared contracts and ship a matching Host before the changed Web UI is deployed. An incompatible Host is shown as an update problem rather than an empty workspace. Desktop without a separately installed Host continues to use its bundled server.

First-release browser support is Chrome and Edge. Installer targets are Windows x64/ARM64, macOS Intel/Apple silicon, and Ubuntu/Debian x64/ARM64. The selected package determines the processor architecture; the user can change the detected choice before downloading. Desktop additionally needs the platform WebView dependency described below. Other browsers and Linux package formats are outside the first-release support matrix.

## Packages

| OS | Host | Desktop | Startup |
|---|---|---|---|
| Windows x64, ARM64 | Inno Setup `.exe` | Inno Setup `.exe` | Host per-user scheduled task at login |
| macOS Intel, Apple silicon | `.pkg` | `.pkg` | Host per-user LaunchAgent at login |
| Ubuntu/Debian x64, ARM64 | `.deb` | `.deb` | Host systemd user service at login |

Linux Desktop requires WebKitGTK (`libwebkit2gtk-4.1-0`) or WPE WebKit. Windows Desktop requires WebView2 Runtime. See [Desktop app](23-desktop.md) for the remaining platform requirements.

Build one platform package on its matching OS:

```text
dotnet run --project build -- package-release --runtime win-x64 --version 1.0.0
dotnet run --project build -- package-release --runtime osx-arm64 --version 1.0.0
dotnet run --project build -- package-release --runtime linux-x64 --version 1.0.0
```

All six runtime combinations are in `.github/workflows/release.yml`. A `v*` tag builds the installers and attaches them to a GitHub Release. A manual workflow run uses its version input to create or update the same Release from the run's commit, so the Web download links can find the installers. Production distribution of Windows and macOS installers still requires code signing; macOS notarization also requires Apple credentials. Those credentials are not configured in this repository.

## Web publishing

`dotnet run --project build -- publish-web` writes `artifacts/web/wwwroot`, including `CNAME`, `.nojekyll`, the public Host address, and a Pages fallback page. `.github/workflows/pages.yml` deploys that directory on pushes to `master` or manual runs.

Repository setup required on GitHub: select **GitHub Actions** as the Pages build source and configure the custom domain `ai.dev-team.org`. Configure the domain DNS with the GitHub Pages records and verify it in repository settings before enforcing HTTPS. The `CNAME` file alone cannot change DNS or repository settings.

Do not make a public release before checking each installer on a clean machine for its OS, browser permission flow, Host startup after login, Desktop attachment, and uninstall. The local Windows build verifies compilation and the HTTP grant flow. Linux x64 publishing, both `.deb` package structures, and both binary startup commands were checked in Ubuntu under WSL. macOS packaging and installation remain unverified until the workflow runs on a macOS runner, and all platforms still need real installation checks.
