namespace AI.Application.Resources;

using AI.Contracts.Resources;

public sealed record ResourceDefinition(ChatResourceRef Reference, long Revision, bool Retired);
