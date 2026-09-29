namespace AI.Application.Tools;

public enum RunCompletionStatus
{
    Complete,
    Blocked
}

/// <param name="IncludePreviousText">
/// Publish the model's last unpublished message as the answer, followed by <paramref name="FinalAnswer"/>
/// when it adds anything. Lets a long answer written as plain text be published without being retyped.
/// </param>
public sealed record RunCompletionDecision(
    RunCompletionStatus Status,
    string FinalAnswer,
    bool IncludePreviousText = false);

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
