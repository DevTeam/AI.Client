namespace AI.Application.Tools;

public enum RunCompletionStatus
{
    Complete,
    Blocked
}

/// <param name="IncludePreviousText">
/// Whether the model's last unpublished message is published ahead of <paramref name="FinalAnswer"/>.
/// Null when the model did not say, which includes it: models asked to retype a long answer wrote a
/// one-line summary of it instead, so the answer itself was lost.
/// </param>
public sealed record RunCompletionDecision(
    RunCompletionStatus Status,
    string FinalAnswer,
    bool? IncludePreviousText = null);

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
