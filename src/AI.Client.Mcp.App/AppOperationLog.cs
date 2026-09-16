namespace AI.Client.Mcp.App;

/// <summary>
/// Remembers the result of every mutation by its <c>operationId</c>, so a model that repeats a
/// call it already made — after a transport error, or because it lost track — gets the original
/// answer back instead of a second chat, a second project or a second queued message.
/// </summary>
/// <remarks>
/// The log lives for as long as the Host process does. That covers the case it exists for, a retry
/// inside one agent run, and deliberately stops short of surviving a restart: making it durable
/// would mean a storage format of its own, and a mutation interrupted by a crash is already
/// visible to the caller through the revision it reads back.
/// </remarks>
public sealed class AppOperationLog : IAppOperationLog
{
    private const int Capacity = 1024;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, AppWriteResult> _results = [];
    private readonly Queue<Guid> _order = new();

    public bool TryGet(Guid operationId, out AppWriteResult? result)
    {
        lock (_gate) return _results.TryGetValue(operationId, out result);
    }

    public void Record(Guid operationId, AppWriteResult result)
    {
        lock (_gate)
        {
            if (!_results.TryAdd(operationId, result)) return;
            _order.Enqueue(operationId);
            while (_order.Count > Capacity) _results.Remove(_order.Dequeue());
        }
    }
}
