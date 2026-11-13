namespace AI.Domain.Projects;

using Common;

public sealed class DirectoryGrant
{
    private readonly HashSet<string> _toolNames;

    public DirectoryGrant(
        DirectoryGrantId id,
        string displayName,
        string canonicalRoot,
        bool recursive,
        IEnumerable<string> toolNames)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainException("Directory grant display name cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(canonicalRoot))
        {
            throw new DomainException("Directory grant root cannot be empty.");
        }

        ArgumentNullException.ThrowIfNull(toolNames);

        _toolNames = new HashSet<string>(
            toolNames.Select(i => i.Trim()).Where(i => i.Length > 0),
            StringComparer.Ordinal);

        if (_toolNames.Count == 0)
        {
            throw new DomainException("Directory grant must allow at least one tool.");
        }

        Id = id;
        DisplayName = displayName.Trim();
        CanonicalRoot = canonicalRoot.Trim();
        Recursive = recursive;
    }

    public DirectoryGrantId Id { get; }

    public string DisplayName { get; }

    public string CanonicalRoot { get; }

    public bool Recursive { get; }

    public IReadOnlySet<string> ToolNames => _toolNames;

    /// <summary>
    /// True when both grants cover one directory. A trailing separator does not count, and case does
    /// not count where the host's file system ignores it, so <c>C:\Work</c> and <c>c:\work\</c> match.
    /// </summary>
    public bool HasSameRoot(DirectoryGrant other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(TrimSeparators(CanonicalRoot), TrimSeparators(other.CanonicalRoot), comparison);
    }

    /// <summary>One grant over this root with the wider access of both: every tool and subdirectories if either had them.</summary>
    public DirectoryGrant MergedWith(DirectoryGrant other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (!HasSameRoot(other)) throw new DomainException("Only grants over one directory can be merged.");
        return new DirectoryGrant(Id, DisplayName, CanonicalRoot, Recursive || other.Recursive, _toolNames.Union(other.ToolNames));
    }

    // A bare root such as "C:\" or "/" keeps its last character so it never compares as empty.
    private static string TrimSeparators(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        return trimmed.Length == 0 || trimmed.EndsWith(':') ? path : trimmed;
    }
}
