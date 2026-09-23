namespace AI.Client.Server.Hosting;

using Microsoft.Extensions.DependencyInjection;

/// <summary>The AI.Client HTTP API, ready to be started by an entry point.</summary>
public interface IAiClientServer
{
    /// <summary>Starts the server and returns once it is listening.</summary>
    /// <param name="services">
    /// The composition that built this server. ASP.NET resolves request services through it, so
    /// every handler sees the same singletons as the server itself.
    /// </param>
    /// <exception cref="Infrastructure.Storage.DataDirectoryInUseException">
    /// Another process serves the same data directory.
    /// </exception>
    Task<IRunningServer> StartAsync(IServiceProviderFactory<IServiceCollection> services, CancellationToken cancellationToken);
}
