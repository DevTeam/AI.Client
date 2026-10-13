namespace AI.Contracts.Chats;

/// <summary>
/// The branch whose run submitted a message, and what the message is meant to be. The project is
/// absent from messages stored before it was recorded; those came from the receiving chat's project.
/// </summary>
public sealed record MessageSender(Guid ChatId, Guid BranchId, string? Intent = null, Guid? ProjectId = null);
