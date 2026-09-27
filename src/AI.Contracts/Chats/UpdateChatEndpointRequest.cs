namespace AI.Contracts.Chats;

public sealed record UpdateChatEndpointRequest(Guid? ConnectionId, long Revision);
