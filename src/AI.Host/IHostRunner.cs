namespace AI.Host;

using Server.Hosting;

/// <summary>Runs the standalone Host: the server, and the tray icon when there is one to show.</summary>
internal interface IHostRunner
{
    /// <returns>The process exit code.</returns>
    int Run(ServerOptions options, bool showTray);
}
