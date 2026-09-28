namespace AI.Host;

using System.CommandLine;
using AI.Contracts;
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
        Description = "Accept paired browsers from the public Web app and serve the local Desktop UI. Bind only to loopback."
    };

    private readonly Option<string> _publicOrigin = new("--public-origin")
    {
        Description = "Origin of the Web app that --public-web accepts. Defaults to the published site; use an HTTP loopback origin to debug the Web app locally.",
        DefaultValueFactory = _ => HostProtocol.PublicWebOrigin
    };

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        rootCommand.Description = "AI standalone host.";
        serverCommandLine.AddTo(rootCommand);
        rootCommand.Options.Add(_urls);
        rootCommand.Options.Add(_serveWeb);
        rootCommand.Options.Add(_publicWeb);
        rootCommand.Options.Add(_publicOrigin);
        rootCommand.SetAction((result, token) =>
            serverRunner.RunAsync(serverCommandLine.Bind(result,
                result.GetValue(_urls) ?? (result.GetValue(_publicWeb) ? HostProtocol.PublicHostAddress.TrimEnd('/') : null),
                result.GetValue(_serveWeb) || result.GetValue(_publicWeb)) with
            {
                PublicWeb = result.GetValue(_publicWeb),
                PublicOrigin = result.GetValue(_publicOrigin)!
            }, token));
        return Task.CompletedTask;
    }
}
