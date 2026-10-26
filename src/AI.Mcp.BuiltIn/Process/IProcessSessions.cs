namespace AI.Mcp.BuiltIn.Process;

/// <summary>Processes owned by this MCP session, shared by both launch variants and their continuation tools.</summary>
public interface IProcessSessions
{
    Task<ProcessResult> RunAsync(ProcessRequest request, int waitTimeoutMs, CancellationToken cancellationToken);
    Task<ProcessResult> WaitAsync(string processId, int waitTimeoutMs, long stdoutOffset, long stderrOffset, CancellationToken cancellationToken);
    Task<ProcessResult> StopAsync(string processId, CancellationToken cancellationToken);
}
