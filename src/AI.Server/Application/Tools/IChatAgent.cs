namespace AI.Application.Tools;

using AI.Application.Chat;
using Contracts.Runs;
using Contracts.Workspace;

/// <summary>
/// One agent turn: streams the model's answer, runs the tool calls it asks for, and reports what
/// the workspace looks like once the turn is over.
/// </summary>
public interface IChatAgent
{
    Task<WorkspaceChangeSet> RunAsync(
        Guid projectId,
        Guid chatId,
        Guid branchId,
        ChatCompletionRequest request,
        Func<ChatCompletionMessage, CancellationToken, Task> persist,
        Func<string, CancellationToken, Task> text,
        Func<ToolActivity?, CancellationToken, Task> activity,
        Func<ChatTransportWait?, CancellationToken, Task> transportActivity,
        Func<AgentTool, string, long, ToolCallPosition, CancellationToken, Task<ToolApprovalAction>> approve,
        CancellationToken cancellationToken,
        // Whether a person can be reached from this run. A background run says so once here rather
        // than refusing tool by tool, because "nobody is watching" is a property of the run.
        bool interactive = true,
        // The branch of the run that delegated this one, when one did. It buys nothing for the run
        // itself: it is what lets the delegating turn report a file total that includes this work.
        Guid? parentBranchId = null,
        // The prose of the model step in flight, chunk by chunk; null starts a new step. It is a
        // live view only: what survives is whatever the step turns into — a preamble, the final
        // answer, or nothing when the protocol rejects the step.
        Func<string?, CancellationToken, Task>? draft = null,
        // How each model request fills the context window, and once more with the final answer
        // counted, so the composer can show what the next request will start from.
        Func<ContextUsage, CancellationToken, Task>? contextUsage = null,
        // The tool the step in flight has started to call, named as soon as the stream names it,
        // before its arguments are complete. A null draft ends it along with the step's prose.
        // The completion protocol's own tool is not reported: it is how the run ends, not work.
        Func<string, CancellationToken, Task>? draftToolCall = null,
        bool overlayPromptsAllowed = true);
}
