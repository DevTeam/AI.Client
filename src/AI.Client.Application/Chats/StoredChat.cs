using AI.Client.Domain.Chats;

namespace AI.Client.Application.Chats;

public sealed record StoredChat(ChatThread Chat, long Revision);
