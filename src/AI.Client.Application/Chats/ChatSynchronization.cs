namespace AI.Client.Application.Chats;

using System.Collections.Concurrent;

public sealed class ChatSynchronization : IChatSynchronization
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _chats = new();

    public async Task<IDisposable> EnterAsync(Guid chatId, CancellationToken cancellationToken)
    {
        var gate = _chats.GetOrAdd(chatId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Lease(gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
