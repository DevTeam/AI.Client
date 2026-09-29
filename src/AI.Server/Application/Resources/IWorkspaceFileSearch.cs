namespace AI.Application.Resources;

using AI.Contracts.Resources;

/// <summary>Finds files and directories by name under the directories a project may read.</summary>
public interface IWorkspaceFileSearch
{
    /// <summary>
    /// The best matches for <paramref name="query"/>: a name, part of one, or a path relative to a
    /// project directory ("src/web/"). An empty query lists the top of each directory.
    /// </summary>
    Task<IReadOnlyList<ResourceSuggestion>> SearchAsync(Guid projectId, string query, int limit,
        CancellationToken cancellationToken);
}
