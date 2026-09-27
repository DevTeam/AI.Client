namespace AI.Mcp.App;

/// <summary>
/// Remembers the result of every applied mutation by its <c>operationId</c>, so a repeated call
/// answers with what it did the first time.
/// </summary>
public interface IAppOperationLog
{
    bool TryGet(Guid operationId, out AppWriteResult? result);

    void Record(Guid operationId, AppWriteResult result);
}
