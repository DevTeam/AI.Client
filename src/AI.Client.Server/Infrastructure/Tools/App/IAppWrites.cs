namespace AI.Client.Mcp.App;

using ModelContextProtocol.Protocol;

/// <summary>
/// The parts every mutating tool shares: replay protection, the change signal, and turning an
/// outcome into a result whose shape does not depend on whether it succeeded.
/// </summary>
public interface IAppWrites
{
    Task<CallToolResult> RunAsync(
        string operation,
        Guid operationId,
        Func<AppWriteBuilder, Task<AppWriteResult>> body);
}
