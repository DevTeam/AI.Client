namespace AI.Application.Chats;

using AI.Contracts.Chats;

public interface IChatService
{
    Task<IReadOnlyList<ChatSummary>> ListAsync(Guid projectId, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);
    Task<ChatDetails?> GetAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);
    Task<ChatDetails?> GetTranscriptAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken);
    /// <summary>Loads one message for review validation without projecting the entire transcript.</summary>
    Task<ChatMessageView?> GetReviewSourceAsync(Guid projectId, Guid chatId, Guid messageId,
        CancellationToken cancellationToken);
    Task<ChatTurnActivity?> GetTurnActivityAsync(Guid projectId, Guid chatId, Guid turnId, Guid branchLeafId, CancellationToken cancellationToken);
    Task<ChatMessageContent?> GetMessageContentAsync(Guid projectId, Guid chatId, Guid messageId, CancellationToken cancellationToken);
    /// <summary>Removes attached review references. False means the chat does not exist.</summary>
    Task<bool> RemoveReviewReferencesAsync(Guid projectId, Guid chatId, Guid reviewId,
        CancellationToken cancellationToken);
    Task<ChatDetails> CreateAsync(Guid projectId, CreateChatRequest request, CancellationToken cancellationToken);
    Task<ChatDetails?> AppendMessageAsync(Guid projectId, Guid chatId, AppendChatMessageRequest request, CancellationToken cancellationToken);
    Task<ChatDetails?> UpdateEndpointAsync(Guid projectId, Guid chatId, UpdateChatEndpointRequest request, CancellationToken cancellationToken);
    Task<ChatDetails?> UpdateApprovalModeAsync(Guid projectId, Guid chatId, UpdateChatApprovalModeRequest request, CancellationToken cancellationToken);
    Task<ChatDetails?> RenameAsync(Guid projectId, Guid chatId, RenameChatRequest request, CancellationToken cancellationToken);
    Task<ChatDetails?> ApplyAutomaticTitleAsync(Guid projectId, Guid chatId, string title, CancellationToken cancellationToken);
    Task<ChatSummary?> PinAsync(Guid projectId, Guid chatId, PinChatRequest request, CancellationToken cancellationToken);
    Task<ChatDetails?> RenameBranchAsync(Guid projectId, Guid chatId, Guid branchId, RenameChatBranchRequest request, CancellationToken cancellationToken);
    Task<ChatDetails?> SetToolPolicyAsync(Guid projectId, Guid chatId, AI.Contracts.Projects.ToolPolicySettings policy, CancellationToken cancellationToken);
    /// <summary>
    /// Reads the chat's kind and state and replaces them in one step under the chat's lease, so a
    /// schedule edited from a widget, a tool and the dispatcher never loses a write. <paramref name="change"/>
    /// returns null to leave the chat as it is. Null when the chat does not exist.
    /// </summary>
    Task<ChatKindChange?> ChangeKindAsync(Guid projectId, Guid chatId, Func<ChatKindState, ChatKindState?> change,
        CancellationToken cancellationToken);
    Task<ChatDetails?> RemoveToolPolicyAsync(Guid projectId, Guid chatId, Guid serverId, string name, string schemaHash, CancellationToken cancellationToken);
}

/// <summary>A chat's kind with its versioned state.</summary>
public sealed record ChatKindState(string Kind, System.Text.Json.JsonElement? State, int Version);

/// <param name="Changed">False when the change function left the chat as it was.</param>
public sealed record ChatKindChange(ChatKindState Current, long Revision, bool Changed);
