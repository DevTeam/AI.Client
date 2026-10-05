namespace AI.Contracts.Chats;

using System.Text.Json;

public sealed record ChatDetails(
    Guid Id,
    Guid ProjectId,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Revision,
    Guid? ConnectionId,
    IReadOnlyList<ChatMessageView> Messages,
    IReadOnlyList<ChatBranchView>? Branches = null,
    IReadOnlyList<AI.Contracts.Projects.ToolPolicySettings>? ToolPolicies = null,
    bool AutoTitlePending = false,
    DateTimeOffset? ArchivedAt = null,
    Guid? ArchiveOperationId = null,
    ToolApprovalMode ApprovalMode = ToolApprovalMode.Ask, string Kind = "conversation",
    JsonElement? KindState = null, string InteractionSurface = "chat", bool AllowChatNavigation = true,
    bool ShowInMainRuns = true, int KindStateVersion = 1);
