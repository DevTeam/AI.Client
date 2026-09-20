namespace AI.Client.Application.Tools;

public enum RunCompletionStatus
{
    Complete,
    Continue,
    Blocked
}

public sealed record RunCompletionDecision(
    RunCompletionStatus Status,
    string? FinalAnswer,
    IReadOnlyList<string> Completed,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Remaining,
    string? NextAction);

/// <summary>
/// Model-facing definition-of-done protocol. It is an application control tool, not an MCP side
/// effect, and is therefore interpreted by the agent itself.
/// </summary>
public interface IRunCompletionProtocol
{
    AgentTool Tool { get; }
    RunCompletionDecision Parse(string arguments);
    string ContinueResult(RunCompletionDecision decision);
    string RejectResult(string reason);
}
