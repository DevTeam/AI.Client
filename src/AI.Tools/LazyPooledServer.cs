namespace AI.Infrastructure.Tools;

using Application.Tools;
using Contracts.Settings;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

/// <summary>
/// An external server a session offers from its cached tool list and leases from the pool only
/// when one of its tools is first called. Every later call of the session uses the same lease; the
/// session's end hands it back.
/// </summary>
internal sealed class LazyPooledServer(
    IExternalMcpServerPool pool,
    McpServerSettings server,
    string fingerprint,
    Func<IReadOnlyList<Tool>, Task> started)
{
    // Progress renews the call's patience, so a slow start is not taken for a tool gone silent,
    // and tells the person what the call is waiting for.
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(5);

    private readonly Lock _gate = new();
    private Task<IExternalMcpLease>? _lease;
    private bool _closed;

    public async ValueTask<McpClient> ConnectAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        Task<IExternalMcpLease> lease;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            // Started once per session and not on the call's token: calls that run together share
            // it, and a call that stops waiting leaves the start to finish for the next one. A start
            // that failed stays failed for the session rather than costing every call its timeout.
            lease = _lease ??= StartAsync();
        }
        while (!lease.IsCompleted)
        {
            progress?.Report(new ToolProgress(0, null, $"Starting the MCP server {server.Name}"));
            await Task.WhenAny(lease, Task.Delay(ProgressInterval, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
        }
        return (await lease).Client;
    }

    private async Task<IExternalMcpLease> StartAsync()
    {
        var lease = await pool.LeaseAsync(server, fingerprint, CancellationToken.None);
        try
        {
            await started(lease.Tools);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Refreshing the cached list is a convenience for later turns; this call goes on.
        }
        return lease;
    }

    public async ValueTask CloseAsync()
    {
        Task<IExternalMcpLease>? lease;
        lock (_gate)
        {
            _closed = true;
            lease = _lease;
        }
        if (lease is null) return;
        // A start still under way is not waited for: the run's answer is saved only once its session
        // is closed. The server goes back to the pool whenever it is ready.
        if (lease.IsCompleted) await ReleaseAsync(lease);
        else _ = ReleaseAsync(lease);
    }

    private static async Task ReleaseAsync(Task<IExternalMcpLease> lease)
    {
        try
        {
            await (await lease).DisposeAsync();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The start failed; there is nothing to hand back.
        }
    }
}
