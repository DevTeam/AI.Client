namespace AI.Host;

using System.CommandLine;
using AI.Contracts;
using Server.CommandLine;

/// <summary>The standalone host: the server options plus where to listen, on the root command.</summary>
internal sealed class StandaloneCommand(
    RootCommand rootCommand,
    IServerCommandLine serverCommandLine,
    IHostRunner hostRunner) : IInitializable
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

    private readonly Option<bool> _noTray = new("--no-tray")
    {
        Description = "With --public-web, do not show the icon in the notification area (the menu bar on macOS)."
    };

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        rootCommand.Description = "AI standalone host.";
        serverCommandLine.AddTo(rootCommand);
        rootCommand.Options.Add(_urls);
        rootCommand.Options.Add(_serveWeb);
        rootCommand.Options.Add(_publicWeb);
        rootCommand.Options.Add(_publicOrigin);
        rootCommand.Options.Add(_noTray);
        // Synchronous, so the action runs on the main thread, which the tray icon's UI needs.
        // The Host installed to run in the background (--public-web) shows an icon so the user
        // knows it is there; a development run of the plain server does not.
        rootCommand.SetAction(result => hostRunner.Run(serverCommandLine.Bind(result,
                result.GetValue(_urls) ?? (result.GetValue(_publicWeb) ? HostProtocol.PublicHostAddress.TrimEnd('/') : null),
                result.GetValue(_serveWeb) || result.GetValue(_publicWeb)) with
            {
                PublicWeb = result.GetValue(_publicWeb),
                PublicOrigin = result.GetValue(_publicOrigin)!
            },
            showTray: result.GetValue(_publicWeb) && !result.GetValue(_noTray)));
        return Task.CompletedTask;
    }
}
