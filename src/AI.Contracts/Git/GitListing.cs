namespace AI.Contracts.Git;

/// <summary>A real Git reference or commit; Value is the full ref name or object id.</summary>
public sealed record GitChoice(string Value, string Label, string? Description = null);

public sealed record GitListing(string RepositoryPath, IReadOnlyList<GitChoice> Items, bool HasMore);
