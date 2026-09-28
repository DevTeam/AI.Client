namespace AI.Desktop;

using Avalonia;
using Server.Hosting;

internal sealed class DesktopRunner(ISharedHostLocator sharedHostLocator) : IDesktopRunner
{
    public int Run(ServerOptions options, bool devTools)
    {
        // A separately installed Host owns the shared data directory. Reuse its UI and API so
        // Desktop and the public Web application see the same projects at the same time.
        var sharedHost = sharedHostLocator.Find(options.DataDirectory);
        if (sharedHost.Address is { } sharedAddress)
        {
            var sharedUi = new UiComposition(new DesktopStart(sharedAddress, null, options.DataDirectory, devTools));
            return AppBuilder.Configure(() => sharedUi.App)
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .StartWithClassicDesktopLifetime([]);
        }

        if (sharedHost.Error is not null)
        {
            var failedUi = new UiComposition(new DesktopStart(null, sharedHost.Error, options.DataDirectory, devTools));
            return AppBuilder.Configure(() => failedUi.App)
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .StartWithClassicDesktopLifetime([]);
        }

        // The server graph depends on the options, so it gets a container of its own, which is
        // also ASP.NET's service provider factory and lives exactly as long as the server.
        using var server = new ServerComposition(options);
        IRunningServer? running = null;
        string? error = null;
        try
        {
            // Before the UI exists, and on a thread-pool thread: nothing the server awaits may be
            // posted back to a UI dispatcher.
            running = Task.Run(() => server.Server.StartAsync(server, CancellationToken.None)).GetAwaiter().GetResult();
        }
#pragma warning disable CA1031 // Whatever stops the server from starting is shown in the window instead of crashing it.
        catch (Exception startError)
#pragma warning restore CA1031
        {
            error = startError.Message;
        }

        try
        {
            var ui = new UiComposition(new DesktopStart(running?.Address, error, options.DataDirectory, devTools));
            return AppBuilder.Configure(() => ui.App)
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .StartWithClassicDesktopLifetime([]);
        }
        finally
        {
            // Off the UI thread: once the UI has shut down, a continuation posted back to its
            // dispatcher would never run and stopping the server would hang the exit.
            if (running is not null)
            {
                Task.Run(() => running.DisposeAsync().AsTask()).GetAwaiter().GetResult();
            }
        }
    }

}
