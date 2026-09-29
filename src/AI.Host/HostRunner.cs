namespace AI.Host;

using Server.Hosting;

internal sealed class HostRunner(IServerRunner serverRunner, IHostTray tray) : IHostRunner
{
    public int Run(ServerOptions options, bool showTray)
    {
        if (!showTray || !tray.IsAvailable)
        {
            return serverRunner.RunAsync(options, CancellationToken.None).GetAwaiter().GetResult();
        }

        using var stop = new CancellationTokenSource();
        // The UI owns this thread (macOS allows no other), so the server runs on the thread pool,
        // where nothing it awaits is posted back to the UI dispatcher.
        var server = Task.Run(() => serverRunner.RunAsync(options, stop.Token));
        try
        {
            tray.Run(server, stop, options.DataDirectory);
        }
#pragma warning disable CA1031 // No tray is no reason to stop serving: the Host goes on without an icon.
        catch (Exception error)
#pragma warning restore CA1031
        {
            Console.Error.WriteLine($"The tray icon is not available: {error.Message}");
        }

        return server.GetAwaiter().GetResult();
    }
}
