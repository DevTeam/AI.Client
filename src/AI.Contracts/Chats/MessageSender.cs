namespace AI.Contracts.Chats;

/// <summary>The branch whose run submitted a message, and what the message is meant to be.</summary>
public sealed record MessageSender(Guid ChatId, Guid BranchId, string? Intent = null);
