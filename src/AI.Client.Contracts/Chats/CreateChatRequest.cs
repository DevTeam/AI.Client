namespace AI.Client.Contracts.Chats;

public sealed record CreateChatRequest(string Title, Guid? ConnectionId = null);
