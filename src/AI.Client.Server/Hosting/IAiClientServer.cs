namespace AI.Client.Server.Hosting;

using Microsoft.Extensions.DependencyInjection;

/// <summary>The AI.Client HTTP API, ready to be started by an entry point.</summary>
public interface IAiClientServer
{
    /// <summary>Runs the server until <paramref name="cancellationToken"/> fires or the host shuts down.</summary>
    /// <param name="services">
    /// The composition that built this server. ASP.NET resolves request services through it, so
    /// every handler sees the same singletons as the server itself.
    /// </param>
    Task RunAsync(IServiceProviderFactory<IServiceCollection> services, CancellationToken cancellationToken);
}
