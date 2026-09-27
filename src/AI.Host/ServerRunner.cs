namespace AI.Host;

using Infrastructure.Storage;
using Server.Hosting;

internal sealed class ServerRunner : IServerRunner
{
    public async Task<int> RunAsync(ServerOptions options, CancellationToken cancellationToken)
    {
        // The server graph depends on the options, which exist only once the command line has been
        // parsed, so it gets a container of its own. That container is also ASP.NET's service
        // provider factory, and it lives exactly as long as the server.
        using var composition = new ServerComposition(options);
        IRunningServer server;
        try
        {
            server = await composition.Server.StartAsync(composition, cancellationToken);
        }
        catch (DataDirectoryInUseException error)
        {
            await Console.Error.WriteLineAsync(error.Message);
            return 2;
        }

        await using (server)
        {
            await server.WaitForShutdownAsync(cancellationToken);
        }

        return 0;
    }
}
