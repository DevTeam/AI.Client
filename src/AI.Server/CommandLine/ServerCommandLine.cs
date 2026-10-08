namespace AI.Server.CommandLine;

using AI.Contracts.FileSystem;
using System.CommandLine;
using Hosting;

public sealed class ServerCommandLine(IPath paths) : IServerCommandLine
{
    private readonly Option<string> _dataDirectory = new("--data-dir")
    {
        Description = "Directory for projects, chats, settings and logs. Defaults to AI_CLIENT_DATA_DIRECTORY, then to the local application data folder.",
        DefaultValueFactory = _ =>
            Environment.GetEnvironmentVariable("AI_CLIENT_DATA_DIRECTORY")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AI")
    };

    private readonly Option<bool> _noBrowse = new("--no-browse")
    {
        Description = "Do not expose the local file system browser to the UI."
    };

    public void AddTo(Command command)
    {
        command.Options.Add(_dataDirectory);
        command.Options.Add(_noBrowse);
    }

    public ServerOptions Bind(ParseResult result, string? urls, bool serveWeb) => new(
        paths.GetFullPath(result.GetValue(_dataDirectory)!),
        urls,
        BrowseEnabled: !result.GetValue(_noBrowse),
        ServeWeb: serveWeb);
}
