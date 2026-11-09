namespace AI.Desktop;

using AI.Infrastructure.Instances;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;

/// <remarks>
/// The window comes as a factory: a window built before <see cref="Initialize"/> loads the theme
/// would miss its styles and brushes. Every window shows the same server; the process ends with
/// the last of them.
/// </remarks>
internal sealed partial class App(Func<MainWindow> mainWindow, IProcessSignals processSignals, IThemePreferenceStore themes,
    IInstanceClaim instance)
    : Application
{
    // Touched on the UI thread only, except the flag the instance channel reads.
    private readonly List<MainWindow> _windows = [];
    private MainWindow? _lastActive;
    private volatile bool _closing;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ApplyTheme(themes.Load());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
            desktop.MainWindow = Open(cascadeFrom: null);
            // The window is about to be shown: this is the UI-ready moment of the startup marker.
            StartupTimeline.UiReady();
            // A signal is a request to quit like any other: the window closes normally, so the
            // server behind it stops and the data directory is released.
            var signals = processSignals.OnTermination(() => Dispatcher.UIThread.Post(() => desktop.Shutdown()));
            // A later launch on the same data directory brings a window back, or opens one more,
            // instead of starting a second process over the same data.
            instance.OnRequest(request =>
            {
                if (_closing) return false;
                Dispatcher.UIThread.Post(() => Handle(request));
                return true;
            });
            desktop.Exit += (_, _) => signals.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private MainWindow Open(MainWindow? cascadeFrom)
    {
        var window = mainWindow();
        if (cascadeFrom is not null) window.CascadeFrom(cascadeFrom);
        window.ThemeRequested += preference =>
        {
            ApplyTheme(preference);
            themes.Save(preference);
        };
        window.NewWindowRequested += () => OpenMore(window);
        window.Activated += (_, _) => _lastActive = window;
        window.Closed += (_, _) =>
        {
            _windows.Remove(window);
            if (_lastActive == window) _lastActive = _windows.LastOrDefault();
            _closing = _windows.Count == 0;
        };
        _windows.Add(window);
        return window;
    }

    private void OpenMore(MainWindow source)
    {
        if (_closing) return;
        var window = Open(source);
        window.Show();
        window.Activate();
    }

    /// <summary>The window used last answers a later launch; a request after the last one closed finds nothing.</summary>
    private void Handle(InstanceRequest request)
    {
        if ((_lastActive ?? _windows.LastOrDefault()) is not { } current) return;
        if (request == InstanceRequest.NewWindow) OpenMore(current);
        else current.BringToFront();
    }

    /// <summary>
    /// The window's brushes live in this application's theme dictionaries, and the system titlebar
    /// takes its light or dark look from the variant, so it is set here rather than on the window. "system" leaves it to the OS, which is what the page does too;
    /// an unknown preference keeps following the OS rather than guessing.
    /// </summary>
    private void ApplyTheme(string preference) =>
        RequestedThemeVariant = preference switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            "darkblue" => DesktopThemes.DarkBlue,
            "gray" => DesktopThemes.Gray,
            "lightgray" => DesktopThemes.LightGray,
            _ => ThemeVariant.Default
        };
}
