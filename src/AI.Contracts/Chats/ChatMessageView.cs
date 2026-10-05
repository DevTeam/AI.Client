namespace AI.Contracts.Chats;

public sealed record ChatMessageView(
    Guid Id,
    Guid? ParentId,
    string Role,
    string Content,
    DateTimeOffset CreatedAt,
    bool IsIncomplete = false,
    IReadOnlyList<Chat.ChatToolCall>? ToolCalls = null,
    string? ToolCallId = null,
    Workspace.WorkspaceChangeSet? WorkspaceChanges = null,
    bool ContentOmitted = false,
    IReadOnlyList<Resources.ChatResource>? Resources = null,
    // Kept even when tool output is omitted from the compact transcript. Null for legacy results
    // without an explicit error flag, or for messages that are not tool results.
    bool? ToolResultIsError = null);
