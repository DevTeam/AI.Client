namespace AI.Client.Desktop;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

/// <remarks>
/// The window comes as a factory: a window built before <see cref="Initialize"/> loads the theme
/// never gets its drawn titlebar template, so the titlebar neither drags nor clicks.
/// </remarks>
internal sealed partial class App(Func<MainWindow> mainWindow, IProcessSignals processSignals) : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = mainWindow();
            // A signal is a request to quit like any other: the window closes normally, so the
            // server behind it stops and the data directory is released.
            var signals = processSignals.OnTermination(() => Dispatcher.UIThread.Post(() => desktop.Shutdown()));
            desktop.Exit += (_, _) => signals.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
