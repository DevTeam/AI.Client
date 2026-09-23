namespace AI.Client.Host;

using Server.Hosting;

internal sealed class ServerRunner : IServerRunner
{
    public async Task RunAsync(ServerOptions options, CancellationToken cancellationToken)
    {
        // The server graph depends on the options, which exist only once the command line has been
        // parsed, so it gets a container of its own. That container is also ASP.NET's service
        // provider factory, and it lives exactly as long as the server.
        using var composition = new ServerComposition(options);
        await composition.Server.RunAsync(composition, cancellationToken);
    }
}
