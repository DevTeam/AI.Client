namespace AI.Client.Desktop;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

internal sealed partial class App(MainWindow mainWindow, IProcessSignals processSignals) : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = mainWindow;
            // A signal is a request to quit like any other: the window closes normally, so the
            // server behind it stops and the data directory is released.
            var signals = processSignals.OnTermination(() => Dispatcher.UIThread.Post(() => desktop.Shutdown()));
            desktop.Exit += (_, _) => signals.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
