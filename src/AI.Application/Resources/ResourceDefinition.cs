namespace AI.Application.Resources;

using AI.Contracts.Resources;

public sealed record ResourceDefinition(ChatResource Reference, long Revision, bool Retired);
