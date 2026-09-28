namespace AI.Application.Tools;

public enum RunCompletionStatus
{
    Complete,
    Blocked
}

public sealed record RunCompletionDecision(
    RunCompletionStatus Status,
    string FinalAnswer);

/// <summary>
/// Model-facing definition-of-done protocol. It is an application control tool, not an MCP side
/// effect, and is therefore interpreted by the agent itself.
/// </summary>
public interface IRunCompletionProtocol
{
    AgentTool Tool { get; }
    RunCompletionDecision Parse(string arguments);
    string RejectResult(string reason);
}
