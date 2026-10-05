namespace AI.Contracts.Runs;

public sealed record ChatRunSnapshot(Guid ProjectId, Guid ChatId, Guid BranchId, ChatRunStatus Status, string StreamingContent,
    IReadOnlyList<QueuedChatMessage> Queue, bool HasUnreadResponse, string? Error, long Revision,
    long ChatRevision = 0, Guid? HeadMessageId = null, ToolApproval? PendingApproval = null, IReadOnlyList<ActiveToolInvocation>? ActiveTools = null,
    RunFailureCode FailureCode = RunFailureCode.None, bool CanRetry = true, long BranchRevision = 0,
    IReadOnlyList<RunRecoveryAction>? RecoveryActions = null,
    Workspace.WorkspaceChangeSet? WorkspaceChanges = null,
    Guid? ActiveMessageId = null,
    UserPrompt? PendingPrompt = null,
    ChatRunWait? Wait = null,
    ChatMessageDelta? MessageDelta = null,
    // The prose of the model step in flight. Unlike StreamingContent it is never persisted and
    // is not the answer: it lets the transcript show what the model is saying before the step
    // ends and becomes a preamble, the final answer, or nothing.
    string? DraftContent = null,
    // The tool the model step in flight has started to call, by its model-facing name, while the
    // call's arguments are still streaming. It marks DraftContent as a preamble rather than a
    // possible answer, and fills the pause before the call becomes an active tool.
    string? DraftToolCall = null,
    // How the last request of this branch filled the model's context window. It lives with the
    // run rather than the chat, so a Host restart forgets it until the branch sends again.
    ContextUsage? Context = null,
    // What the current turn of this branch has used so far, or the last turn once it ended —
    // subtasks, routing and compaction included. In memory like Context; the ledger keeps the
    // durable copy.
    Usage.TurnTokenUsage? TurnUsage = null, string Kind = "conversation",
    string InteractionSurface = "chat", bool ShowInMainRuns = true);

/// <summary>
/// A bounded, self-contained tail of messages persisted while a run is active. Every append names
/// the revision it starts from, so a client can merge it only when no revision was missed and fall
/// back to loading the chat when the tail is no longer long enough.
/// </summary>
public sealed record ChatMessageDelta(IReadOnlyList<ChatMessageAppend> Appends);

public sealed record ChatMessageAppend(
    long BaseRevision,
    long Revision,
    Chats.ChatMessageView Message);
