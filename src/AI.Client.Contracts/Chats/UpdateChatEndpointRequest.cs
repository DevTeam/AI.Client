namespace AI.Client.Contracts.Chats;

public sealed record UpdateChatEndpointRequest(Guid? EndpointProfileId, long Revision);
