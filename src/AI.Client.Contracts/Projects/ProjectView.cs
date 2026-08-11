namespace AI.Client.Contracts.Projects;

public sealed record ProjectView(
    Guid Id,
    string Name,
    string Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
