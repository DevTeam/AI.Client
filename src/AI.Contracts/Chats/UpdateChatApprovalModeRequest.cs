namespace AI.Contracts.Chats;

/// <summary>
/// Carries no revision on purpose: the mode is most often changed while a run is holding the chat
/// open for a confirmation, when every revision the page knows is already stale.
/// </summary>
public sealed record UpdateChatApprovalModeRequest(ToolApprovalMode Mode);
