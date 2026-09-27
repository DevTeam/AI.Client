namespace AI.Desktop;

using System.CommandLine;
using Server.CommandLine;

/// <summary>The desktop app: the shared server options plus its own, on the root command.</summary>
internal sealed class DesktopCommand(
    RootCommand rootCommand,
    IServerCommandLine serverCommandLine,
    IDesktopRunner desktopRunner) : IInitializable
{
    private readonly Option<bool> _devTools = new("--dev-tools")
    {
        Description = "Enable the web view's developer tools."
    };

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        rootCommand.Description = "AI desktop app.";
        serverCommandLine.AddTo(rootCommand);
        rootCommand.Options.Add(_devTools);
        // Loopback only and a port the OS picks: nothing outside this machine can reach the
        // server, and a second app on the same port cannot stop this one from starting.
        // The window owns Ctrl+C and SIGTERM: it closes, and closing it stops the server.
        rootCommand.SetAction(result => desktopRunner.Run(
            serverCommandLine.Bind(result, "http://127.0.0.1:0", serveWeb: true) with { StopOnProcessSignals = false },
            result.GetValue(_devTools)));
        return Task.CompletedTask;
    }
}
