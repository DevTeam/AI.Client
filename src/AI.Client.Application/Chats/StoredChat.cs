namespace AI.Client.Application.Chats;

using AI.Client.Domain.Chats;

public sealed record StoredChat(ChatThread Chat, long Revision);
