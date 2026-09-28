namespace AI.Host;

using Infrastructure.Storage;
using Server.Hosting;

internal sealed class ServerRunner : IServerRunner
{
    public async Task<int> RunAsync(ServerOptions options, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // A Desktop-only installation may still be running its embedded server when the
            // background Host is installed. Wait for that process to release the shared data
            // directory, then become its owner without creating a second data store.
            using var composition = new ServerComposition(options);
            IRunningServer server;
            try
            {
                server = await composition.Server.StartAsync(composition, cancellationToken);
            }
            catch (DataDirectoryInUseException error)
            {
                await Console.Error.WriteLineAsync(error.Message);
                if (!options.PublicWeb) return 2;
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return 0;
                }
                continue;
            }

            await using (server)
            {
                await server.WaitForShutdownAsync(cancellationToken);
            }

            return 0;
        }

        return 0;
    }
}
