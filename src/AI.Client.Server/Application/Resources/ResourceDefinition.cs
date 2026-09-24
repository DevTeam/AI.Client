namespace AI.Client.Application.Resources;

using AI.Client.Contracts.Resources;

public sealed record ResourceDefinition(ChatResourceRef Reference, long Revision, bool Retired);
