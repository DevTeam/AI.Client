namespace AI.Host;

using System.CommandLine;
using Server.CommandLine;

/// <summary>The standalone host: the server options plus where to listen, on the root command.</summary>
internal sealed class StandaloneCommand(
    RootCommand rootCommand,
    IServerCommandLine serverCommandLine,
    IServerRunner serverRunner) : IInitializable
{
    private readonly Option<string?> _urls = new("--urls")
    {
        Description = "Addresses to listen on, separated by semicolons. Defaults to ASPNETCORE_URLS or the launch profile."
    };

    private readonly Option<bool> _serveWeb = new("--serve-web")
    {
        Description = "Also serve the UI, from the same address as the API."
    };

    private readonly Option<bool> _publicWeb = new("--public-web")
    {
        Description = "Accept paired browsers from https://ai.dev-team.org and serve the local Desktop UI. Bind only to loopback."
    };

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        rootCommand.Description = "AI standalone host.";
        serverCommandLine.AddTo(rootCommand);
        rootCommand.Options.Add(_urls);
        rootCommand.Options.Add(_serveWeb);
        rootCommand.Options.Add(_publicWeb);
        rootCommand.SetAction((result, token) =>
            serverRunner.RunAsync(serverCommandLine.Bind(result,
                result.GetValue(_urls) ?? (result.GetValue(_publicWeb) ? "http://127.0.0.1:52173" : null),
                result.GetValue(_serveWeb) || result.GetValue(_publicWeb)) with
            {
                PublicWeb = result.GetValue(_publicWeb)
            }, token));
        return Task.CompletedTask;
    }
}
