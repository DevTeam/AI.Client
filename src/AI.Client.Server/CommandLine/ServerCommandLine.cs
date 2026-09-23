namespace AI.Client.Server.CommandLine;

using System.CommandLine;
using Hosting;

public sealed class ServerCommandLine : IServerCommandLine
{
    private readonly Option<DirectoryInfo> _dataDirectory = new("--data-dir")
    {
        Description = "Directory for projects, chats, settings and logs. Defaults to AI_CLIENT_DATA_DIRECTORY, then to the local application data folder.",
        DefaultValueFactory = _ => new DirectoryInfo(
            Environment.GetEnvironmentVariable("AI_CLIENT_DATA_DIRECTORY")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AI.Client"))
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
        result.GetValue(_dataDirectory)!.FullName,
        urls,
        BrowseEnabled: !result.GetValue(_noBrowse),
        ServeWeb: serveWeb);
}
