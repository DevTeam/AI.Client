namespace AI.Client.Host;

using Server.Hosting;

/// <summary>Builds the server for the options the command line produced and runs it.</summary>
internal interface IServerRunner
{
    /// <returns>The process exit code.</returns>
    Task<int> RunAsync(ServerOptions options, CancellationToken cancellationToken);
}
