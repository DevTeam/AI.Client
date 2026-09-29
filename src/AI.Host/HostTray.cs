namespace AI.Host;

using Avalonia;
using Avalonia.Controls;

internal sealed class HostTray(IHostStatus status, IWebAppLauncher webApp, IBrowserOpener shell) : IHostTray
{
    // Avalonia 12 has only an X11 backend on Linux; a Wayland session still exports DISPLAY for XWayland.
    public bool IsAvailable =>
        !OperatingSystem.IsLinux() || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));

    public void Run(Task<int> server, CancellationTokenSource stop, string dataDirectory) =>
        AppBuilder.Configure(() => new TrayApp(server, stop, dataDirectory, status, webApp, shell))
            .UsePlatformDetect()
            // A menu bar item only: no Dock icon and no application menu for an app without windows.
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .LogToTrace()
            .StartWithClassicDesktopLifetime([], ShutdownMode.OnExplicitShutdown);
}
