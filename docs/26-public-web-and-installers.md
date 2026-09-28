# Public Web and installers

## User flow

`https://ai.dev-team.org/` serves the static Blazor WebAssembly client from GitHub Pages. It does not store projects or credentials. Chrome and Edge connect to a Host running on the same computer at `http://127.0.0.1:52173/`. The browser may ask for Local Network Access ([Chrome](https://developer.chrome.com/blog/local-network-access), [Edge](https://learn.microsoft.com/en-us/deployedge/ms-edge-local-network-access)). The page offers both Host and Desktop downloads from the latest GitHub Release, with a selectable platform.

The Host starts in the background after user login. The user opens the local confirmation page and grants the published origin access. The Host issues a random browser grant, stores only its hash in the local data directory, and requires it for public-origin API calls. Disconnect in the Web app revokes that grant. No .NET runtime installation is needed: all packages publish self-contained binaries.

Open the public Web app with **HTTPS**. The Host deliberately rejects `http://ai.dev-team.org`, even if the Host is running. On Windows, the installer registers a per-user scheduled task named `AI.Client.Host`; no Start menu window is needed. If it is stopped, run `Start-ScheduledTask -TaskName AI.Client.Host` in PowerShell. `http://127.0.0.1:52173/api/health` should then return the Host's status in the same computer's browser.

The Desktop app uses the installed Host when it is available. It shows an error if an installed Host is stopped or incompatible, instead of opening a second server on the same data directory. Without a separately installed Host, Desktop keeps its embedded server. Local Host and Web development keep their existing behavior.

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
