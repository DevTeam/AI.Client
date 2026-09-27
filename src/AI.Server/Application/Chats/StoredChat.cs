namespace AI.Application.Chats;

using AI.Domain.Chats;

public sealed record StoredChat(ChatThread Chat, long Revision);
