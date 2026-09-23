namespace AI.Client.Server.CommandLine;

using System.CommandLine;
using Hosting;

/// <summary>
/// The options every executable that hosts the server accepts, so that <c>--data-dir</c> means the
/// same thing for the standalone host and the desktop app.
/// </summary>
public interface IServerCommandLine
{
    void AddTo(Command command);

    ServerOptions Bind(ParseResult result, string? urls, bool serveWeb);
}
