namespace AI.Server.Hosting;

using Application.Chats;
using Application.Runs;
using Microsoft.Extensions.Hosting;

internal sealed class ChatRunHostedService(IChatRunDispatcher dispatcher, IChatKindPolicyRegistry kinds) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _startupCancellation = new();
    private Task _startupTask = Task.CompletedTask;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await dispatcher.WarmUpAsync(cancellationToken);
        _startupTask = Task.Run(() => StartChatKindsAsync(_startupCancellation.Token), CancellationToken.None);
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
