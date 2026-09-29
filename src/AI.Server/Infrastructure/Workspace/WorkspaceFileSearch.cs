namespace AI.Infrastructure.Workspace;

using System.Collections.Concurrent;
using AI.Application.Projects;
using AI.Application.Resources;
using AI.Contracts.Resources;

/// <summary>
/// Name search over a project's readable directories for the composer's "@" list. Each directory is
/// walked once and the listing kept for a short while: the list asks again on every keystroke.
/// </summary>
public sealed class WorkspaceFileSearch(IProjectService projects, IProjectPathAccess access, IClock clock) : IWorkspaceFileSearch
{
    /// <summary>Build output, dependencies and tool state: never what a person means by a name.</summary>
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".hg", ".svn", ".vs", ".idea", "bin", "obj", "node_modules", "packages", "__pycache__", ".venv"
    };

    private const int EntryLimit = 100_000;
    private static readonly TimeSpan IndexLifetime = TimeSpan.FromSeconds(30);
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly ConcurrentDictionary<string, Index> _indexes = new(OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public async Task<IReadOnlyList<ResourceSuggestion>> SearchAsync(Guid projectId, string query, int limit,
        CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("Project not found.");
        limit = Math.Clamp(limit, 1, 200);
        query = query.Trim().Replace('\\', '/').TrimStart('/');
        if (query.StartsWith("./", StringComparison.Ordinal)) query = query[2..];
        var found = new List<(ResourceSuggestion Suggestion, int Rank)>();
        foreach (var grant in project.DirectoryGrants.Where(grant => grant.ToolNames.Contains("read", StringComparer.OrdinalIgnoreCase)))
        {
            string root;
            try { root = access.ResolveLinks(grant.CanonicalRoot); }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException) { continue; }
            if (!Directory.Exists(root)) continue;
            var index = IndexOf(root, grant.Recursive, cancellationToken);
            foreach (var entry in index.Entries)
            {
                if (Rank(entry, query) is not { } rank) continue;
                found.Add((new ResourceSuggestion(entry.IsDirectory ? ChatResourceKind.Directory : ChatResourceKind.File,
                    Path.Combine(root, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar)), entry.RelativePath,
                    access.AccessOf(project, root)), rank));
            }
        }
        return found
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.Suggestion.RelativePath.Count(character => character == '/'))
            .ThenBy(item => item.Suggestion.RelativePath.Length)
            .ThenBy(item => item.Suggestion.RelativePath, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(item => item.Suggestion.Path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Take(limit)
            .Select(item => item.Suggestion)
            .ToArray();
    }

    /// <summary>
    /// Lower is better; null leaves the entry out. A query with a slash is a path: it matches from
    /// the start of the relative path, then from the start of any segment. Otherwise it is a name.
    /// </summary>
    private static int? Rank(Entry entry, string query)
    {
        if (query.Length == 0) return entry.Depth == 0 ? (entry.IsDirectory ? 0 : 1) : null;
        var path = entry.RelativePath;
        if (query.Contains('/'))
        {
            if (path.StartsWith(query, PathComparison)) return 0;
            if (path.Contains("/" + query, PathComparison)) return 1;
            return path.Contains(query, PathComparison) ? 2 : null;
        }
        var name = entry.Name;
        if (name.Equals(query, PathComparison)) return 0;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 1;
        for (var at = name.IndexOf(query, StringComparison.OrdinalIgnoreCase); at > 0;
             at = name.IndexOf(query, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            if (!char.IsLetterOrDigit(name[at - 1]) || char.IsUpper(name[at]) && char.IsLower(name[at - 1])) return 2;
        }
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase)) return 3;
        if (path.Contains(query, StringComparison.OrdinalIgnoreCase)) return 4;
        return IsSubsequence(name, query) ? 5 : null;
    }

    private static bool IsSubsequence(string text, string query)
    {
        var at = 0;
        foreach (var character in text)
        {
            if (at < query.Length && char.ToLowerInvariant(character) == char.ToLowerInvariant(query[at])) at++;
        }
        return at == query.Length;
    }

    private Index IndexOf(string root, bool recursive, CancellationToken cancellationToken)
    {
        var key = $"{recursive}|{root}";
        var now = clock.UtcNow;
        if (_indexes.TryGetValue(key, out var cached) && now - cached.BuiltAt < IndexLifetime) return cached;
        var index = new Index(now, Walk(root, recursive, cancellationToken));
        _indexes[key] = index;
        return index;
    }

    /// <summary>Breadth first, so a directory too large to list whole still offers its upper levels.</summary>
    private static List<Entry> Walk(string root, bool recursive, CancellationToken cancellationToken)
    {
        var entries = new List<Entry>();
        var pending = new Queue<(string Directory, string Relative, int Depth)>();
        pending.Enqueue((root, string.Empty, 0));
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
        while (pending.TryDequeue(out var current) && entries.Count < EntryLimit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerable<FileSystemInfo> children;
            try { children = new DirectoryInfo(current.Directory).EnumerateFileSystemInfos("*", options).ToArray(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { continue; }
            foreach (var child in children)
            {
                var relative = current.Relative.Length == 0 ? child.Name : $"{current.Relative}/{child.Name}";
                var isDirectory = child is DirectoryInfo;
                if (isDirectory && SkippedDirectories.Contains(child.Name)) continue;
                entries.Add(new Entry(child.Name, relative, isDirectory, current.Depth));
                // A link is listed but not followed: it can lead outside the grant, or in a circle.
                if (isDirectory && recursive && child.LinkTarget is null)
                    pending.Enqueue((child.FullName, relative, current.Depth + 1));
            }
        }
        return entries;
    }

    private sealed record Entry(string Name, string RelativePath, bool IsDirectory, int Depth);

    private sealed record Index(DateTimeOffset BuiltAt, IReadOnlyList<Entry> Entries);
}
