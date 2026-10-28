namespace AI.Infrastructure.Tools;

using Application.Users;
using Contracts.Settings;
using Domain.Users;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

/// <summary>A running external server held by one session; disposing it hands the server back.</summary>
public interface IExternalMcpLease : IAsyncDisposable
{
    McpClient Client { get; }

    IReadOnlyList<Tool> Tools { get; }
}

/// <summary>
/// Keeps user-configured MCP servers running between turns, so a server is started once rather
/// than once per turn.
/// </summary>
public interface IExternalMcpServerPool
{
    /// <summary>
    /// A running server for this configuration: an idle one when there is one, otherwise a new one.
    /// A lease is exclusive — two runs never share a server process at the same time — and the
    /// start goes on when the caller stops waiting, so the server is ready for the next turn.
    /// </summary>
    Task<IExternalMcpLease> LeaseAsync(McpServerSettings server, string fingerprint, CancellationToken cancellationToken);
}

/// <remarks>
/// An idle server is stopped after <see cref="IdleTimeout"/>, and at most
/// <see cref="MaxIdlePerServer"/> are kept per configuration. Servers are kept per user, so one
/// user's server never sees another user's calls, and per fingerprint, so a changed configuration
/// starts a new server and retires the old ones.
/// </remarks>
public sealed class ExternalMcpServerPool(IExternalMcpConnector connector, IUserContext users)
    : IExternalMcpServerPool, IAsyncDisposable
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(15);
    private const int MaxIdlePerServer = 2;

    private readonly Lock _gate = new();
    private readonly List<Idle> _idle = [];
    private readonly CancellationTokenSource _disposed = new();

    public async Task<IExternalMcpLease> LeaseAsync(McpServerSettings server, string fingerprint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);
        var key = new Key(users.Current, server.Id, fingerprint);
        while (Take(key) is { } idle)
        {
            // A server that exited while idle is no use to anyone; the next one, or a new one, is.
            if (!idle.Client.Completion.IsCompleted) return new Lease(this, key, idle);
            await idle.DisposeAsync();
        }

        var start = connector.ConnectAsync(server, null, _disposed.Token);
        try
        {
            return new Lease(this, key, await start.WaitAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _ = AdoptAsync(key, start);
            throw;
        }
    }

    /// <summary>A start nobody waits for any more still yields a server, kept for the next turn.</summary>
    private async Task AdoptAsync(Key key, Task<ExternalMcpConnection> start)
    {
        try
        {
            await ReturnAsync(key, await start);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The turn that asked has gone; the next one starts the server again and sees the error.
        }
    }

    private ExternalMcpConnection? Take(Key key)
    {
        List<ExternalMcpConnection> retired;
        ExternalMcpConnection? taken = null;
        lock (_gate)
        {
            // Servers of this user's earlier configuration of the same server will not be asked for again.
            var stale = _idle.Where(item => item.Key.User == key.User && item.Key.ServerId == key.ServerId
                && item.Key.Fingerprint != key.Fingerprint).ToArray();
            foreach (var item in stale) _idle.Remove(item);
            retired = stale.Select(item => item.Connection).ToList();
            var index = _idle.FindLastIndex(item => item.Key == key);
            if (index >= 0)
            {
                taken = _idle[index].Connection;
                _idle.RemoveAt(index);
            }
        }
        foreach (var connection in retired) _ = DisposeQuietlyAsync(connection);
        return taken;
    }

    private async ValueTask ReturnAsync(Key key, ExternalMcpConnection connection)
    {
        ExternalMcpConnection? surplus = null;
        Idle? idle = null;
        lock (_gate)
        {
            if (_disposed.IsCancellationRequested || connection.Client.Completion.IsCompleted)
                surplus = connection;
            else
            {
                idle = new Idle(key, connection);
                _idle.Add(idle);
                var same = _idle.Where(item => item.Key == key).ToArray();
                if (same.Length > MaxIdlePerServer)
                {
                    _idle.Remove(same[0]);
                    surplus = same[0].Connection;
                }
            }
        }
        if (surplus is not null) await DisposeQuietlyAsync(surplus);
        if (idle is not null) _ = ExpireAsync(idle);
    }

    private async Task ExpireAsync(Idle idle)
    {
        try
        {
            await Task.Delay(IdleTimeout, _disposed.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        lock (_gate)
            if (!_idle.Remove(idle)) return;
        await DisposeQuietlyAsync(idle.Connection);
    }

    private static async Task DisposeQuietlyAsync(ExternalMcpConnection connection)
    {
        try
        {
            await connection.DisposeAsync();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // A server that fails to stop cleanly has stopped serving all the same.
        }
    }

    public async ValueTask DisposeAsync()
    {
        Idle[] idle;
        lock (_gate)
        {
            if (_disposed.IsCancellationRequested) return;
            _disposed.Cancel();
            idle = _idle.ToArray();
            _idle.Clear();
        }
        await Task.WhenAll(idle.Select(item => DisposeQuietlyAsync(item.Connection)));
        _disposed.Dispose();
    }

    private sealed record Key(UserId User, Guid ServerId, string Fingerprint);

    /// <summary>One idle server; compared by reference, so an expiry finds exactly the entry it was set for.</summary>
    private sealed class Idle(Key key, ExternalMcpConnection connection)
    {
        public Key Key { get; } = key;

        public ExternalMcpConnection Connection { get; } = connection;
    }

    private sealed class Lease(ExternalMcpServerPool pool, Key key, ExternalMcpConnection connection) : IExternalMcpLease
    {
        private int _returned;

        public McpClient Client => connection.Client;

        public IReadOnlyList<Tool> Tools => connection.Tools;

        public ValueTask DisposeAsync() =>
            Interlocked.Exchange(ref _returned, 1) == 0 ? pool.ReturnAsync(key, connection) : ValueTask.CompletedTask;
    }
}
