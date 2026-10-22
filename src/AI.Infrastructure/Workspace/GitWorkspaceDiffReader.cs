namespace AI.Infrastructure.Workspace;

using System.Globalization;
using System.Text;
using AI.Application.Resources;
using AI.Contracts.FileSystem;
using AI.Infrastructure.Credentials;
/// <summary>
/// Asks the installed git for a repository's uncommitted changes. The diff is kept with the message
/// that attached it, so it is bounded; the model runs git itself when it needs more.
/// </summary>
public sealed class GitWorkspaceDiffReader(ICommandRunner commands, IFileSystem files, IPath paths) : IWorkspaceDiffReader
{
    public const int CharacterLimit = 96 * 1024;
    private const int UntrackedLimit = 200;
    private const int SearchDepth = 4;
    private const int DirectoryLimit = 5_000;
    private const int RepositoryLimit = 50;

    /// <summary>Build output and dependencies: large, and never where a checkout lives.</summary>
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".hg", ".svn", ".vs", ".idea", "bin", "obj", "node_modules", "packages", "__pycache__", ".venv"
    };

    public string? FindRepository(string directory)
    {
        var result = Git(directory, "rev-parse", "--show-toplevel");
        if (result is not { ExitCode: 0 }) return null;
        var root = result.StandardOutput.Trim();
        return root.Length == 0 ? null : paths.GetFullPath(root);
    }

    public IReadOnlyList<string> FindNestedRepositories(string directory)
    {
        var found = new List<string>();
        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((directory, 0));
        var visited = 0;
        // The platform's own walk rules are kept: an unreadable directory is passed over and system
        // entries are left out. Whether a link may be entered is decided per entry below, because
        // the reparse attribute alone also marks a cloud-sync placeholder that is not a link.
        var options = new FileEnumerationOptions(SkipInaccessible: true, AttributesToSkip: FileAttributes.System);
        while (pending.TryDequeue(out var current) && visited++ < DirectoryLimit && found.Count < RepositoryLimit)
        {
            IReadOnlyList<FileSystemEntry> children;
            try
            {
                children = files.ListEntriesAsync(current.Path, options, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { continue; }
            foreach (var child in children)
            {
                if (!child.IsDirectory || IsLink(child) || SkippedDirectories.Contains(child.Name)) continue;
                // ".git" is a directory in a clone and a file in a worktree or submodule.
                if (MarkerExists(Path.Combine(child.Path, ".git"))) found.Add(child.Path);
                if (current.Depth + 1 < SearchDepth) pending.Enqueue((child.Path, current.Depth + 1));
            }
        }
        return found;
    }

    public IReadOnlyList<string> ChangedFiles(string directory)
    {
        var result = Git(directory, "status", "--porcelain=v1", "--untracked-files=all", "--", ".");
        if (result is not { ExitCode: 0 }) return [];
        return result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Length > 3)
            .Select(line => line[3..].Trim())
            .ToArray();
    }

    public string ReadDiff(string directory)
    {
        var diff = Git(directory, "--no-pager", "diff", "HEAD", "--relative", "--no-color", "--no-ext-diff", "--", ".");
        // A repository without a commit has no HEAD: then everything is staged or untracked.
        if (diff is not { ExitCode: 0 }) diff = Git(directory, "--no-pager", "diff", "--cached", "--relative", "--no-color", "--no-ext-diff", "--", ".");
        if (diff is not { ExitCode: 0 })
            throw new InvalidOperationException($"git could not read the changes in {directory}: {diff?.StandardError.Trim() ?? "git is not installed"}");
        var untracked = Git(directory, "ls-files", "--others", "--exclude-standard", "--", ".");
        var text = new StringBuilder(diff.StandardOutput);
        var newFiles = untracked is { ExitCode: 0 }
            ? untracked.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
        if (newFiles.Length > 0)
        {
            text.Append("\nUntracked files (not in the diff):\n");
            foreach (var file in newFiles.Take(UntrackedLimit)) text.Append("  ").Append(file).Append('\n');
            if (newFiles.Length > UntrackedLimit) text.Append(CultureInfo.InvariantCulture, $"  ... and {newFiles.Length - UntrackedLimit} more\n");
        }
        if (text.Length == 0) return "No uncommitted changes.";
        if (text.Length <= CharacterLimit) return text.ToString();
        return text.ToString(0, CharacterLimit) + "\n[cut: the diff is longer; run git diff for the rest]";
    }

    /// <summary>
    /// Whether a repository marker is there, as a file or as a directory: one probe answers both.
    /// The enclosing walk is synchronous and the probe completes without waiting for anything, so
    /// the completed task is bridged rather than the interface widened.
    /// </summary>
    private bool MarkerExists(string path) =>
        files.GetEntryAsync(path, CancellationToken.None).GetAwaiter().GetResult() is not null;

    /// <summary>
    /// Whether the entry is a link this walk must not enter. The reparse attribute is only the
    /// cheap pre-filter: a Windows cloud-sync placeholder carries it with nothing to resolve, and
    /// skipping on the attribute alone would stop descending into directories the product walked
    /// before. The decision is the resolved path differing from the entry's own canonical one,
    /// bridged from the completed task because this walk is synchronous.
    /// </summary>
    private bool IsLink(FileSystemEntry entry)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) == 0) return false;
        try
        {
            var own = paths.TrimEndingDirectorySeparator(paths.GetFullPath(entry.Path));
            var resolved = paths.TrimEndingDirectorySeparator(
                files.ResolveLinkTargetAsync(entry.Path, CancellationToken.None).GetAwaiter().GetResult());
            return !string.Equals(resolved, own, paths.Comparison);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
                                          or ArgumentException or NotSupportedException)
        {
            // A path that cannot be resolved is treated as one this walk must not enter.
            return true;
        }
    }

    private CommandResult? Git(string directory, params string[] arguments) =>
        commands.Run("git", ["-C", directory, "-c", "core.quotepath=off", .. arguments]);
}
