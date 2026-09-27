// ReSharper disable NotAccessedPositionalProperty.Global

namespace AI.Contracts.Projects;

public sealed record ProjectSummary(Guid Id, string Name, string Description, DateTimeOffset UpdatedAt, long Revision, Guid? ConnectionId = null);
