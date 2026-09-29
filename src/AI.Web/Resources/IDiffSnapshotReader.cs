namespace AI.Web.Resources;

using AI.Contracts.Workspace;

/// <summary>
/// Reads back the changes a "@diff" reference captured when its message was sent. The Host keeps
/// them as the text of <c>git diff</c> in the reference's excerpt; this turns that text into the
/// change set the transcript already knows how to draw.
/// </summary>
public interface IDiffSnapshotReader
{
    /// <param name="excerpt">The captured text, or null for a reference sent before it was kept.</param>
    /// <param name="root">The repository directory the paths in the diff are relative to.</param>
    DiffSnapshot? Read(string? excerpt, string root);
}

/// <param name="IsCut">
/// The Host stopped at its size cap, or listed only some of the untracked files: there were more
/// changes than <see cref="Changes"/> holds.
/// </param>
public sealed record DiffSnapshot(WorkspaceChangeSet Changes, bool IsCut);
