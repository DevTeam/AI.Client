namespace AI.Infrastructure.Workspace;

using System.Collections.Concurrent;
using AI.Application.Projects;
using AI.Application.Resources;
using AI.Contracts.Projects;
using AI.Contracts.Resources;

/// <summary>
/// Search over a project's readable directories for the composer's "@" list, answered without
/// waiting on the disk wherever it can be:
/// <list type="bullet">
/// <item>an empty word and a path ("src/", "App/src/We") read one directory each, nothing more;</item>
/// <item>a name is looked up in an index of every directory, built in the background — started by
/// <see cref="Warm"/> when a project opens, or by the first search — and searched as far as it has
/// got, with <see cref="ResourceSearchResult.Complete"/> false until it is whole.</item>
/// </list>
/// An index older than <see cref="IndexLifetime"/> keeps answering while its replacement is built.
/// </summary>
public sealed class WorkspaceFileSearch(IProjectService projects, IProjectPathAccess access, IClock clock) : IWorkspaceFileSearch
{
    /// <summary>Build output, dependencies and tool state: never what a person means by a name.</summary>
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".hg", ".svn", ".vs", ".idea", "bin", "obj", "node_modules", "packages", "__pycache__", ".venv"
    };

    private const int EntryLimit = 100_000;
    private const int PublishEvery = 4_000;
    private static readonly TimeSpan IndexLifetime = TimeSpan.FromSeconds(30);
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly EnumerationOptions ListingOptions = new() { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };

    private readonly ConcurrentDictionary<string, Index> _indexes = new(PathComparer);

    public async Task Warm(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("Project not found.");
        foreach (var root in RootsOf(project)) IndexOf(root);
    }

    public async Task<ResourceSearchResult> SearchAsync(Guid projectId, string query, int limit,
        CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("Project not found.");
        limit = Math.Clamp(limit, 1, 200);
        query = query.Trim().Replace('\\', '/').TrimStart('/');
        if (query.StartsWith("./", StringComparison.Ordinal)) query = query[2..];
        var roots = RootsOf(project);
        var found = new List<(ResourceSuggestion Suggestion, int Rank)>();
        var complete = true;
        if (query.Length == 0)
        {
            // The top of each directory, and the index for the name the user is about to type.
            foreach (var root in roots)
            {
                IndexOf(root);
                found.AddRange(List(project, root, root.Path, string.Empty, string.Empty));
            }
        }
        else if (query.Contains('/'))
        {
            foreach (var root in roots) found.AddRange(ListPath(project, root, query));
            // "Pages/Home" names no directory at the top; it may still end a deeper path.
            if (found.Count == 0) complete = SearchIndex(project, roots, query, found);
        }
        else complete = SearchIndex(project, roots, query, found);
        var items = found
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.Suggestion.RelativePath.Count(character => character == '/'))
            .ThenBy(item => item.Suggestion.RelativePath.Length)
            .ThenBy(item => item.Suggestion.RelativePath, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(item => item.Suggestion.Path, PathComparer)
            .Take(limit)
            .Select(item => item.Suggestion)
            .ToArray();
        return new ResourceSearchResult(items, complete);
    }

    private List<Root> RootsOf(ProjectDetails project)
    {
        var roots = new List<Root>();
        foreach (var grant in project.DirectoryGrants.Where(grant => grant.ToolNames.Contains("read", StringComparer.OrdinalIgnoreCase)))
        {
            try
            {
                var path = access.ResolveLinks(grant.CanonicalRoot);
                if (Directory.Exists(path)) roots.Add(new Root(path, grant.DisplayName, grant.Recursive));
            }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
            {
                // A directory that cannot be reached now offers nothing; the others still answer.
            }
        }
        return roots;
    }

    /// <summary>
    /// "src/We" lists "src" and keeps what starts with "We". The first segment may be the project
    /// directory's own name — "@App/" opens the directory called App in the project — and then the
    /// paths it returns keep that name in front, so the next Tab goes on from there.
    /// </summary>
    private List<(ResourceSuggestion, int)> ListPath(ProjectDetails project, Root root, string query)
    {
        var slash = query.LastIndexOf('/');
        var directory = query[..slash];
        var name = query[(slash + 1)..];
        var listed = new List<(ResourceSuggestion, int)>();
        var segments = directory.Split('/', 2);
        if (root.Name.Length > 0 && segments[0].Equals(root.Name, StringComparison.OrdinalIgnoreCase))
        {
            var below = segments.Length > 1 ? segments[1] : string.Empty;
            listed.AddRange(List(project, root, Combine(root.Path, below), $"{segments[0]}/", name));
        }
        listed.AddRange(List(project, root, Combine(root.Path, directory), string.Empty, name));
        return listed;
    }

    /// <summary>The children of one directory under a root, filtered by the start of <paramref name="name"/>.</summary>
    private List<(ResourceSuggestion, int)> List(ProjectDetails project, Root root, string directory, string prefix, string name)
    {
        var full = Path.GetFullPath(directory);
        // A ".." in what was typed must not climb out of the directory the project may read.
        if (!Inside(full, root.Path) || !Directory.Exists(full) || !root.Recursive && !string.Equals(full, root.Path, PathComparison))
            return [];
        FileSystemInfo[] children;
        try { children = new DirectoryInfo(full).EnumerateFileSystemInfos("*", ListingOptions).ToArray(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return []; }
        var result = new List<(ResourceSuggestion, int)>();
        var rootAccess = access.AccessOf(project, root.Path);
        foreach (var child in children)
        {
            var isDirectory = child is DirectoryInfo;
            if (isDirectory && SkippedDirectories.Contains(child.Name)) continue;
            int rank;
            if (name.Length == 0) rank = isDirectory ? 0 : 1;
            else if (child.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)) rank = isDirectory ? 0 : 1;
            else if (child.Name.Contains(name, StringComparison.OrdinalIgnoreCase)) rank = 2;
            else continue;
            var relative = prefix + Path.GetRelativePath(root.Path, child.FullName).Replace(Path.DirectorySeparatorChar, '/');
            result.Add((new ResourceSuggestion(isDirectory ? ChatResourceKind.Directory : ChatResourceKind.File,
                child.FullName, relative, rootAccess), rank));
        }
        return result;
    }

    /// <summary>Searches every root's index as far as it is built; false while one is still being built.</summary>
    private bool SearchIndex(ProjectDetails project, List<Root> roots, string query,
        List<(ResourceSuggestion, int)> found)
    {
        var complete = true;
        foreach (var root in roots)
        {
            var index = IndexOf(root);
            var (entries, whole) = index.Snapshot();
            complete &= whole;
            var rootAccess = access.AccessOf(project, root.Path);
            foreach (var entry in entries)
            {
                if (Rank(entry, query) is not { } rank) continue;
                found.Add((new ResourceSuggestion(entry.IsDirectory ? ChatResourceKind.Directory : ChatResourceKind.File,
                    Path.Combine(root.Path, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar)), entry.RelativePath,
                    rootAccess), rank));
            }
        }
        return complete;
    }

    /// <summary>
    /// Lower is better; null leaves the entry out. A query with a slash is a path: it matches from
    /// the start of the relative path, then from the start of any segment. Otherwise it is a name.
    /// </summary>
    private static int? Rank(Entry entry, string query)
    {
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

    /// <summary>
    /// The index to search now. A missing one starts building; a stale whole one keeps answering
    /// while a fresh one builds behind it, and is replaced once that one is whole.
    /// </summary>
    private Index IndexOf(Root root)
    {
        var key = $"{root.Recursive}|{root.Path}";
        var now = clock.UtcNow;
        var current = _indexes.GetOrAdd(key, _ => Index.Start(root, now, null, _indexes, key));
        if (current.IsStale(now, IndexLifetime)) current.Refresh(root, now, _indexes, key);
        return current;
    }

    private static string Combine(string root, string relative) =>
        relative.Length == 0 ? root : Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    private static bool Inside(string path, string root) =>
        string.Equals(path, root, PathComparison)
        || path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, PathComparison);

    private sealed record Root(string Path, string Name, bool Recursive);

    private sealed record Entry(string Name, string RelativePath, bool IsDirectory, int Depth);

    /// <summary>
    /// One root's listing. The walk publishes what it has every few thousand entries, so a search
    /// never waits on it and never sees a list being written to.
    /// </summary>
    private sealed class Index
    {
        private Entry[] _published = [];
        private volatile bool _complete;
        private int _refreshing;

        private Index(DateTimeOffset startedAt) => StartedAt = startedAt;

        private DateTimeOffset StartedAt { get; }

        public (IReadOnlyList<Entry> Entries, bool Complete) Snapshot()
        {
            // Complete is read first: a whole index publishes its last entries before saying so.
            var complete = _complete;
            return (Volatile.Read(ref _published), complete);
        }

        public bool IsStale(DateTimeOffset now, TimeSpan lifetime) => _complete && now - StartedAt >= lifetime;

        public static Index Start(Root root, DateTimeOffset now, Index? previous,
            ConcurrentDictionary<string, Index> indexes, string key)
        {
            var index = new Index(now);
            _ = Task.Run(() =>
            {
                try { index.Walk(root); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // What was listed stays searchable; the next stale check walks again.
                }
                finally { index._complete = true; }
                // A refresh takes over only once whole: until then the old listing is the better answer.
                if (previous is not null) indexes.TryUpdate(key, index, previous);
            });
            return index;
        }

        public void Refresh(Root root, DateTimeOffset now, ConcurrentDictionary<string, Index> indexes, string key)
        {
            if (Interlocked.Exchange(ref _refreshing, 1) == 0) Start(root, now, this, indexes, key);
        }

        /// <summary>Breadth first, so a directory too large to list whole still offers its upper levels.</summary>
        private void Walk(Root root)
        {
            var entries = new List<Entry>();
            var pending = new Queue<(string Directory, string Relative, int Depth)>();
            pending.Enqueue((root.Path, string.Empty, 0));
            var published = 0;
            while (pending.TryDequeue(out var current) && entries.Count < EntryLimit)
            {
                FileSystemInfo[] children;
                try { children = new DirectoryInfo(current.Directory).EnumerateFileSystemInfos("*", ListingOptions).ToArray(); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { continue; }
                foreach (var child in children)
                {
                    var relative = current.Relative.Length == 0 ? child.Name : $"{current.Relative}/{child.Name}";
                    var isDirectory = child is DirectoryInfo;
                    if (isDirectory && SkippedDirectories.Contains(child.Name)) continue;
                    entries.Add(new Entry(child.Name, relative, isDirectory, current.Depth));
                    // A link is listed but not followed: it can lead outside the grant, or in a circle.
                    if (isDirectory && root.Recursive && child.LinkTarget is null)
                        pending.Enqueue((child.FullName, relative, current.Depth + 1));
                }
                if (entries.Count - published < PublishEvery) continue;
                Volatile.Write(ref _published, entries.ToArray());
                published = entries.Count;
            }
            Volatile.Write(ref _published, entries.ToArray());
        }
    }
}
