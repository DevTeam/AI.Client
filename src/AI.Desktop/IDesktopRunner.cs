namespace AI.Desktop;

using Server.Hosting;

/// <summary>Starts the server, shows the window over it, and stops both together.</summary>
internal interface IDesktopRunner
{
    /// <param name="newWindow">Ask an instance already running on the data directory for one more window rather than the front.</param>
    /// <returns>The process exit code.</returns>
    int Run(ServerOptions options, bool devTools, bool newWindow);
}
