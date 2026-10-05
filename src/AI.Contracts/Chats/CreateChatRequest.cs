namespace AI.Contracts.Chats;

using System.Text.Json;

public sealed record CreateChatRequest(string Title, Guid? ConnectionId = null, bool AutoTitlePending = false,
    ToolApprovalMode ApprovalMode = ToolApprovalMode.Ask, string Kind = "conversation", JsonElement? KindState = null,
    int KindStateVersion = 1);
