namespace AI.Client.Contracts.Projects;

public sealed record ProjectSummary(Guid Id, string Name, string Description, DateTimeOffset UpdatedAt, long Revision);
