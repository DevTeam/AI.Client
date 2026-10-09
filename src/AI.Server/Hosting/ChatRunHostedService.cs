namespace AI.Server.Hosting;

using Application.Chats;
using Application.Runs;
using Microsoft.Extensions.Hosting;

internal sealed class ChatRunHostedService(IChatRunDispatcher dispatcher, IChatKindPolicyRegistry kinds) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _startupCancellation = new();
    private Task _startupTask = Task.CompletedTask;

    /// <summary>
    /// Returned as soon as the background work is under way, never after it: the Host is listening
    /// the moment this task completes, and until it does the desktop window cannot be shown at all.
    /// Run recovery only reads what was left in the data directory, so its cost grows with the
    /// stored runs and used to hold the whole start-up for as long as that read took.
    /// </summary>
    /// <remarks>
    /// The order between recovery and the chat kinds is unchanged — recovery still finishes first,
    /// so a kind never starts against a half-recovered run set — and <see cref="StopAsync"/> still
    /// drains both.
    /// </remarks>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _startupTask = Task.Run(
            async () =>
            {
                await dispatcher.WarmUpAsync(_startupCancellation.Token);
                await StartChatKindsAsync(_startupCancellation.Token);
            },
            CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _startupCancellation.CancelAsync();
        try { await _startupTask.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) when (_startupCancellation.IsCancellationRequested) { }
        try
        {
            foreach (var kind in kinds.All) await kind.OnHostStoppingAsync(cancellationToken);
        }
        finally { await dispatcher.ShutdownAsync(cancellationToken); }
    }

    public void Dispose() => _startupCancellation.Dispose();

    private async Task StartChatKindsAsync(CancellationToken cancellationToken)
    {
        foreach (var kind in kinds.All)
        {
            try { await kind.OnHostStartedAsync(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { }
        }
    }
}
