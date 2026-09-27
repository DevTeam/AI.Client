namespace AI.Contracts.Chats;

public sealed record PinChatRequest(bool IsPinned, long Revision);
