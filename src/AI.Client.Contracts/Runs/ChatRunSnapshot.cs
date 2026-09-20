namespace AI.Client.Contracts.Runs;

public sealed record ChatRunSnapshot(Guid ProjectId, Guid ChatId, Guid BranchId, ChatRunStatus Status, string StreamingContent,
    IReadOnlyList<QueuedChatMessage> Queue, bool HasUnreadResponse, string? Error, long Revision,
    long ChatRevision = 0, Guid? HeadMessageId = null, ToolApproval? PendingApproval = null, IReadOnlyList<ActiveToolInvocation>? ActiveTools = null,
    RunFailureCode FailureCode = RunFailureCode.None, bool CanRetry = true, long BranchRevision = 0,
    IReadOnlyList<RunRecoveryAction>? RecoveryActions = null,
    Workspace.WorkspaceChangeSet? WorkspaceChanges = null,
    Guid? ActiveMessageId = null,
    UserPrompt? PendingPrompt = null,
    bool StreamingToolCallsStarted = false,
    ChatRunWait? Wait = null,
    string? IntermediateContent = null);
