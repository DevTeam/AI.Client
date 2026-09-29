namespace AI.Application.Resources;

using AI.Contracts.Resources;

/// <summary>Finds files and directories by name under the directories a project may read.</summary>
public interface IWorkspaceFileSearch
{
    /// <summary>Starts indexing the project's directories, so the first name typed after "@" is found at once.</summary>
    Task Warm(Guid projectId, CancellationToken cancellationToken);

    /// <summary>
    /// The best matches for <paramref name="query"/>: a name, part of one, or a path relative to a
    /// project directory ("src/web/"), which may start with the directory's own name. An empty
    /// query lists the top of each directory. A name search answers from an index still being
    /// built, and says so.
    /// </summary>
    Task<ResourceSearchResult> SearchAsync(Guid projectId, string query, int limit, CancellationToken cancellationToken);
}
