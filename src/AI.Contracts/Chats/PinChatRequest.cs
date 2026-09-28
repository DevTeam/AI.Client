namespace AI.Contracts.Chats;

/// <summary>Pins, unpins or moves a chat among the pinned ones.</summary>
/// <param name="BeforeChatId">The pinned chat this one goes in front of; null places it last.
/// Ignored when unpinning.</param>
public sealed record PinChatRequest(bool IsPinned, long Revision, Guid? BeforeChatId = null);
