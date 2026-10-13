namespace AI.Contracts.Projects;

/// <summary>The order the person gave the projects in the sidebar, first to last.</summary>
public sealed record ReorderProjectsRequest(IReadOnlyList<Guid> ProjectIds);
