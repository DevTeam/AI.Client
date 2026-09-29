namespace AI.Application.Resources;

/// <summary>
/// The uncommitted changes under a directory of a git work tree. Only that directory is read, so
/// a project granted part of a repository never sees the rest of it.
/// </summary>
public interface IWorkspaceDiffReader
{
    /// <summary>The work tree that contains <paramref name="directory"/>, or null when it is not in one or git is not installed.</summary>
    string? FindRepository(string directory);

    /// <summary>Paths git reports as changed or untracked under the directory.</summary>
    IReadOnlyList<string> ChangedFiles(string directory);

    /// <summary>The unified diff of the directory against HEAD, paths relative to it, untracked files listed after it.</summary>
    string ReadDiff(string directory);
}
