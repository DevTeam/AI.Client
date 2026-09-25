namespace AI.Client.Desktop;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;

/// <remarks>
/// The window comes as a factory: a window built before <see cref="Initialize"/> loads the theme
/// never gets its drawn titlebar template, so the titlebar neither drags nor clicks.
/// </remarks>
internal sealed partial class App(Func<MainWindow> mainWindow, IProcessSignals processSignals, IThemePreferenceStore themes)
    : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ApplyTheme(themes.Load());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = mainWindow();
            window.ThemeRequested += preference =>
            {
                ApplyTheme(preference);
                themes.Save(preference);
            };
            desktop.MainWindow = window;
            // A signal is a request to quit like any other: the window closes normally, so the
            // server behind it stops and the data directory is released.
            var signals = processSignals.OnTermination(() => Dispatcher.UIThread.Post(() => desktop.Shutdown()));
            desktop.Exit += (_, _) => signals.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// The titlebar's brushes live in this application's theme dictionaries, so the variant is set
    /// here rather than on the window. "system" leaves it to the OS, which is what the page does too.
    /// </summary>
    private void ApplyTheme(string preference) =>
        RequestedThemeVariant = preference switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
}
