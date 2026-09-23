namespace AI.Client.Desktop;

using Server.Hosting;

/// <summary>Starts the server, shows the window over it, and stops both together.</summary>
internal interface IDesktopRunner
{
    /// <returns>The process exit code.</returns>
    int Run(ServerOptions options, bool devTools);
}
