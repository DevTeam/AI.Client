namespace AI.Client.Server.Hosting;

using Microsoft.Extensions.Hosting;

/// <summary>
/// A lifetime that leaves the process signals alone, for a server that lives inside an app which
/// handles them itself. The default console lifetime would stop the server on SIGTERM while the
/// app that owns it kept running.
/// </summary>
internal sealed class EmbeddedHostLifetime : IHostLifetime
{
    public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
