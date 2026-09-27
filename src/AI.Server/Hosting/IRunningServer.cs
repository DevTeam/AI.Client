namespace AI.Server.Hosting;

/// <summary>A started server. Disposing it stops the server and releases the data directory.</summary>
public interface IRunningServer : IAsyncDisposable
{
    /// <summary>The address actually bound, with the real port when port 0 was asked for.</summary>
    Uri Address { get; }

    /// <summary>Completes when the server shuts down, by itself or through <paramref name="cancellationToken"/>.</summary>
    Task WaitForShutdownAsync(CancellationToken cancellationToken);
}
