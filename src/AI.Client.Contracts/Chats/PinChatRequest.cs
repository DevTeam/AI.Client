namespace AI.Client.Contracts.Chats;

public sealed record PinChatRequest(bool IsPinned, long Revision);
