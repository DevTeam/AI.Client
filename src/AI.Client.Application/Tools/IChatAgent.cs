namespace AI.Client.Application.Tools;

using Contracts.Chat;
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
        Func<CancellationToken, Task> toolCallsStarted,
        Func<ToolActivity?, CancellationToken, Task> activity,
        Func<AgentTool, string, long, ToolCallPosition, CancellationToken, Task<ToolApprovalAction>> approve,
        CancellationToken cancellationToken,
        // Whether a person can be reached from this run. A background run says so once here rather
        // than refusing tool by tool, because "nobody is watching" is a property of the run.
        bool interactive = true,
        // The branch of the run that delegated this one, when one did. It buys nothing for the run
        // itself: it is what lets the delegating turn report a file total that includes this work.
        Guid? parentBranchId = null);
}
