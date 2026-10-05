using AI.Application.Chats;
using AI.Domain.Chats;
using AI.Domain.Projects;
using AI.Application.Resources;
using System.Text.Json;
using System.Text.Json.Serialization;
// ReSharper disable UseCollectionExpression

namespace AI.Infrastructure.Storage;

public sealed class ChatDocumentSerializer : IChatDocumentSerializer
{
    private const int SchemaVersion = 9;
    private const int PreviousSchemaVersion = 6;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Serialize(ChatThread chat, long revision) => JsonSerializer.Serialize(new ChatDocument(
        SchemaVersion,
        revision,
        chat.Id.Value,
        chat.ProjectId.Value,
        chat.Title,
        chat.CreatedAt,
        chat.UpdatedAt,
        chat.ConnectionId?.Value,
        chat.Messages.Select(message => new ChatMessageDocument(
            message.Id.Value,
            message.ParentId?.Value,
            message.Role,
            message.Content,
            message.CreatedAt,
            message.IsIncomplete, message.ToolCalls, message.ToolCallId,
            ToDocument(message.WorkspaceChanges), ResourceReferences.ToContract(message.Resources))).ToArray(),
        chat.Branches.Select(branch => new BranchDocument(branch.Id, branch.HeadMessageId?.Value, branch.Title,
            branch.ParentBranchId, branch.RootMessageId?.Value, branch.Revision)).ToArray(),
        chat.ToolPolicies.Select(policy => new ToolPolicyDocument(policy.Tool.ServerId.Value, policy.Tool.Name,
            policy.Tool.SchemaHash, policy.Decision, policy.MaxCallsPerRun, policy.Timeout)).ToArray(),
        chat.IsPinned,
        chat.PinnedAt,
        chat.LastActivityAt,
        chat.PinOrder,
        chat.AutoTitlePending, chat.ArchivedAt, chat.ArchiveOperationId, chat.ApprovalMode,
        Kind: chat.Kind.Value, KindState: chat.KindState, KindStateVersion: chat.KindStateVersion), Options);

    public string SerializeSummary(ChatThread chat, long revision) => JsonSerializer.Serialize(new ChatSummaryDocument(
        SchemaVersion,
        revision,
        chat.Id.Value,
        chat.ProjectId.Value,
        chat.Title,
        chat.UpdatedAt,
        chat.IsPinned,
        chat.PinnedAt,
        chat.LastActivityAt,
        chat.BranchCount,
        chat.PinOrder,
        chat.Messages.Count == 0, chat.ArchivedAt, chat.ArchiveOperationId, Kind: chat.Kind.Value), Options);

    public StoredChat Deserialize(string json)
    {
        var document = JsonSerializer.Deserialize<ChatDocument>(json, Options)
            ?? throw new JsonException("Chat document is empty.");
        if (document.SchemaVersion is not (5 or PreviousSchemaVersion or 7 or 8 or SchemaVersion) || document.Revision < 0)
        {
            throw new JsonException("Chat document schema or revision is invalid.");
        }

        var chat = new ChatThread(
            new ChatId(document.Id),
            new ProjectId(document.ProjectId),
            document.Title,
            document.CreatedAt,
            document.ConnectionId is { } endpointId ? new ConnectionId(endpointId) : null,
            kind: LegacyKind(document.Kind, document.IsGuide, document.GuideMode), kindState:
            document.Kind is null && document.IsGuide
                ? JsonSerializer.SerializeToElement(new { mode = document.GuideMode ?? "show" }) : document.KindState,
            kindStateVersion: document.KindStateVersion);
        foreach (var message in document.Messages.OrderBy(item => item.CreatedAt))
        {
            chat.AddMessage(new ChatMessage(
                new ChatMessageId(message.Id),
                message.ParentId is { } parentId ? new ChatMessageId(parentId) : null,
                message.Role,
                message.Content,
                message.CreatedAt,
                message.IsIncomplete, message.ToolCalls, message.ToolCallId,
                ToDomain(message.WorkspaceChanges), ResourceReferences.ToDomain(message.Resources),
                allowEmptyAfterResourceRemoval: true), message.CreatedAt);
        }
        chat.RestoreBranches(document.Branches.Select(branch => new ChatBranch(branch.Id,
            branch.HeadMessageId is { } head ? new ChatMessageId(head) : null, branch.Title,
            branch.ParentBranchId, branch.RootMessageId is { } root ? new ChatMessageId(root) : null, branch.Revision)));
        foreach (var policy in document.ToolPolicies ?? [])
            chat.SetToolPolicy(new ToolPolicy(new ToolIdentity(new McpServerId(policy.ServerId), policy.Name,
                policy.SchemaHash), policy.Decision, policy.MaxCallsPerRun, policy.Timeout), document.UpdatedAt);
        chat.Rename(document.Title, document.UpdatedAt);
        chat.RestoreAutoTitlePending(document.AutoTitlePending);
        chat.RestorePinState(document.IsPinned, document.PinnedAt, document.PinOrder);
        chat.RestoreActivity(document.LastActivityAt);
        chat.RestoreArchiveState(document.ArchivedAt, document.ArchiveOperationId);
        chat.RestoreApprovalMode(document.ApprovalMode);

        return new StoredChat(chat, document.Revision);
    }

    public StoredChatSummary DeserializeSummary(string json)
    {
        var document = JsonSerializer.Deserialize<ChatSummaryDocument>(json, Options)
            ?? throw new JsonException("Chat document is empty.");
        if (document.SchemaVersion is not (5 or PreviousSchemaVersion or 7 or 8 or SchemaVersion) || document.Revision < 0)
        {
            throw new JsonException("Chat document schema or revision is invalid.");
        }

        return new StoredChatSummary(
            new ChatId(document.Id),
            new ProjectId(document.ProjectId),
            document.Title,
            document.UpdatedAt,
            document.Revision,
            document.LastActivityAt == default ? document.UpdatedAt : document.LastActivityAt,
            document.IsPinned,
            document.PinnedAt,
            document.BranchCount ?? 0,
            document.BranchCount is not null && document.SchemaVersion == SchemaVersion,
            document.IsPinned ? document.PinOrder : null,
            document.IsEmpty, document.ArchivedAt, document.ArchiveOperationId,
            document.Kind is { } kind ? new ChatKind(kind) : document.IsGuide ? ChatKind.Guide : ChatKind.Conversation);
    }

    private static ChatKind LegacyKind(string? kind, bool isGuide, string? guideMode) =>
        kind is { } known ? new ChatKind(known)
        : isGuide ? ChatKind.Guide : guideMode == "demo" ? ChatKind.Demo : ChatKind.Conversation;

    private sealed record ChatDocument(
        // ReSharper disable once MemberHidesStaticFromOuterClass
        int SchemaVersion,
        long Revision,
        Guid Id,
        Guid ProjectId,
        string Title,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        Guid? ConnectionId,
        ChatMessageDocument[] Messages,
        BranchDocument[] Branches,
        ToolPolicyDocument[]? ToolPolicies = null,
        bool IsPinned = false,
        DateTimeOffset? PinnedAt = null,
        DateTimeOffset LastActivityAt = default,
        string? PinOrder = null,
        bool AutoTitlePending = false,
        DateTimeOffset? ArchivedAt = null,
        Guid? ArchiveOperationId = null,
        // Absent from documents written before chats had a mode, which read as the old behaviour.
        ChatApprovalMode ApprovalMode = ChatApprovalMode.Ask,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool IsGuide = false,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? GuideMode = null,
        string? Kind = null, JsonElement? KindState = null, int KindStateVersion = 1);

    // Deliberately contains only sidebar fields. System.Text.Json skips MessageIds without
    // materialising message nodes, so listing chats stays proportional to the small manifests
    // rather than the complete transcript history.
    private sealed record ChatSummaryDocument(
        int SchemaVersion,
        long Revision,
        Guid Id,
        Guid ProjectId,
        string Title,
        DateTimeOffset UpdatedAt,
        bool IsPinned = false,
        DateTimeOffset? PinnedAt = null,
        DateTimeOffset LastActivityAt = default,
        // Only ever "alternative branches" — the main branch is the chat itself. Null identifies
        // a summary written before this field existed, allowing the repository to migrate only
        // those manifests without loading full documents for chats that genuinely have no forks.
        int? BranchCount = null,
        string? PinOrder = null,
        // A chat is created before its first message is written. Manifests from before this
        // field existed read as non-empty, so no old chat disappears from the sidebar.
        bool IsEmpty = false,
        DateTimeOffset? ArchivedAt = null,
        Guid? ArchiveOperationId = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool IsGuide = false,
        string? Kind = null);

    private sealed record ToolPolicyDocument(Guid ServerId, string Name, string SchemaHash,
        ToolPolicyDecision Decision, int? MaxCallsPerRun, TimeSpan? Timeout);

    private sealed record BranchDocument(Guid Id, Guid? HeadMessageId, string Title, Guid? ParentBranchId,
        Guid? RootMessageId, long Revision);

    private static WorkspaceChangeDocument? ToDocument(ChatWorkspaceChangeSet? changes) => changes is null
        ? null
        : new WorkspaceChangeDocument(
            changes.Files.Select(file => new FileChangeDocument(
                file.Path, file.Kind, file.Additions, file.Deletions, file.PreviousPath,
                file.Diff, file.IsBinary, file.Confidence)).ToArray(),
            changes.Additions,
            changes.Deletions);

    private static ChatWorkspaceChangeSet? ToDomain(WorkspaceChangeDocument? changes) => changes is null
        ? null
        : new ChatWorkspaceChangeSet(
            changes.Files.Select(file => new ChatFileChange(
                file.Path, file.Kind, file.Additions, file.Deletions, file.PreviousPath,
                file.Diff, file.IsBinary, file.Confidence)).ToArray(),
            changes.Additions,
            changes.Deletions);

    private sealed record WorkspaceChangeDocument(
        FileChangeDocument[] Files,
        int Additions,
        int Deletions);

    private sealed record FileChangeDocument(
        string Path,
        ChatFileChangeKind Kind,
        int? Additions,
        int? Deletions,
        string? PreviousPath = null,
        string? Diff = null,
        bool IsBinary = false,
        ChatFileChangeConfidence Confidence = ChatFileChangeConfidence.Measured);

    private sealed record ChatMessageDocument(
        Guid Id,
        Guid? ParentId,
        ChatMessageRole Role,
        string Content,
        DateTimeOffset CreatedAt,
        bool IsIncomplete = false,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<ChatToolCall>? ToolCalls = null,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        string? ToolCallId = null,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        WorkspaceChangeDocument? WorkspaceChanges = null,
        IReadOnlyList<AI.Contracts.Resources.ChatResource>? Resources = null);
}
