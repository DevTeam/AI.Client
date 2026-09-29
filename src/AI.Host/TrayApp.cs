namespace AI.Host;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

/// <summary>
/// The background Host's only UI: an icon that says it is running and lets the user open AI Client
/// or quit. There are no windows, so there is no theme to load either.
/// </summary>
internal sealed class TrayApp(
    Task<int> server,
    CancellationTokenSource stop,
    string dataDirectory,
    IHostStatus status,
    IWebAppLauncher webApp,
    IBrowserOpener shell) : Application
{
    private const string ProductName = "AI Client Host";
    private readonly NativeMenuItem _state = new() { IsEnabled = false };
    private readonly NativeMenuItem _open = new("Open AI Client");

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _open.Click += (_, _) => Open();
            var folder = new NativeMenuItem("Open data folder");
            // The shell opens a folder the same way it opens an address.
            folder.Click += (_, _) => shell.Open(dataDirectory);
            var quit = new NativeMenuItem("Quit " + ProductName);
            quit.Click += (_, _) =>
            {
                quit.IsEnabled = false;
                // The server stops and releases the data directory; its completion ends this UI.
                stop.Cancel();
            };

            TrayIcon icon;
            using (var png = typeof(TrayApp).Assembly.GetManifestResourceStream("AI.Host.app-icon.png")
                             ?? throw new InvalidOperationException("The tray icon is not embedded."))
            {
                icon = new TrayIcon
                {
                    Icon = new WindowIcon(png),
                    Menu = [_state, new NativeMenuItemSeparator(), _open, folder, new NativeMenuItemSeparator(), quit]
                };
            }

            icon.Clicked += (_, _) => Open();
            TrayIcon.SetIcons(this, [icon]);

            void OnStatusChanged(HostState state) => Dispatcher.UIThread.Post(() => Show(icon, state));
            status.Changed += OnStatusChanged;
            Show(icon, status.Current);
            // Whether it stopped on Quit, on a signal or with an error, a Host without a server is over.
            server.ContinueWith(_ => Dispatcher.UIThread.Post(() =>
            {
                status.Changed -= OnStatusChanged;
                icon.Dispose();
                desktop.Shutdown();
            }), TaskScheduler.Default);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void Show(TrayIcon icon, HostState state)
    {
        var text = state.Phase switch
        {
            HostPhase.Running => $"Running at {state.Address}",
            HostPhase.WaitingForDataDirectory => "Waiting for AI Client Desktop to close",
            _ => "Starting…"
        };
        _state.Header = text;
        _open.IsEnabled = state.Phase == HostPhase.Running;
        icon.ToolTipText = $"{ProductName}: {text}";
    }

    private void Open()
    {
        if (status.Current is not { Phase: HostPhase.Running, Address: { } address }) return;
        // Off the UI thread: pairing is an HTTP round trip to this same process.
        _ = Task.Run(() => webApp.OpenAsync(address, CancellationToken.None));
    }
}
