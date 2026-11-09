namespace AI.Desktop;

using AI.Infrastructure.Instances;
using Avalonia;
using Server.Hosting;

internal sealed class DesktopRunner(ISharedHostLocator sharedHostLocator, ISingleInstance singleInstance) : IDesktopRunner
{
    public int Run(ServerOptions options, bool devTools, bool newWindow)
    {
        // First of all: the process already running on this data directory brings up a window (or
        // opens one more) instead of this launch looking for a Host, starting a server or showing
        // the lock error. A first launch opens its one window either way.
        using var instance = singleInstance.Claim(options.DataDirectory,
            newWindow ? InstanceRequest.NewWindow : InstanceRequest.Activate);
        if (instance.IsForwarded)
        {
            return 0;
        }

        // A separately installed Host owns the shared data directory. Reuse its UI and API so
        // Desktop and the public Web application see the same projects at the same time.
        var sharedHost = sharedHostLocator.Find(options.DataDirectory);
        if (sharedHost.Address is { } sharedAddress)
        {
            // The server was not started here: the shared Host answered, and that is this run's
            // server-ready moment.
            StartupTimeline.HostReused();
            var sharedUi = new UiComposition(new DesktopStart(sharedAddress, null, options.DataDirectory, devTools), instance);
            return AppBuilder.Configure(() => sharedUi.App)
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .StartWithClassicDesktopLifetime([]);
        }

        if (sharedHost.Error is not null)
        {
            StartupTimeline.ServerUnavailable();
            var failedUi = new UiComposition(new DesktopStart(null, sharedHost.Error, options.DataDirectory, devTools, sharedHost.UpdateAddress), instance);
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
            if (running is not null)
            {
                StartupTimeline.ServerStarted();
            }
        }
#pragma warning disable CA1031 // Whatever stops the server from starting is shown in the window instead of crashing it.
        catch (Exception startError)
#pragma warning restore CA1031
        {
            error = startError.Message;
            StartupTimeline.ServerUnavailable();
        }

        try
        {
            var ui = new UiComposition(new DesktopStart(running?.Address, error, options.DataDirectory, devTools), instance);
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
