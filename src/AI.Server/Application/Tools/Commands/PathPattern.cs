namespace AI.Application.Tools.Commands;

using AI.Contracts.FileSystem;

/// <summary>A path a call names, and where it lies in the project.</summary>
/// <param name="Full">The full path.</param>
/// <param name="Relative">
/// Its segments below the project directory that holds it — empty for that directory itself — or
/// null when no project directory holds it.
/// </param>
public sealed record PathTarget(string Full, IReadOnlyList<string>? Relative);

/// <summary>The paths one file tool call changes, in the order its arguments name them.</summary>
public sealed record PathCall(IReadOnlyList<PathTarget> Targets);

/// <summary>
/// A path rule's pattern: a path below the project's directories with <c>/</c> between its parts
/// (<c>docs/**</c>, <c>src/*.cs</c>), or a full path that matches only there. <c>*</c> and <c>?</c>
/// mask characters inside one part; <c>**</c> as a whole part stands for any number of parts, none
/// included.
/// </summary>
public sealed class PathPattern : IRulePattern
{
    public const int MaxLength = 2_000;

    private readonly string[] _segments;
    private readonly bool _absolute;
    private readonly string _root;
    private readonly bool _ignoreCase;

    private PathPattern(string text, string root, string[] segments, bool absolute, bool ignoreCase)
    {
        _root = root;
        Text = text;
        _segments = segments;
        _absolute = absolute;
        _ignoreCase = ignoreCase;
    }

    public string Text { get; }

    public bool IsExact => !_segments.Any(Masks.IsMask);

    public (int Literal, int Closed, int Characters) Specificity =>
        (_segments.Count(segment => !Masks.IsMask(segment)), _segments.Contains("**") ? 0 : 1,
            _segments.Sum(segment => segment.Count(character => character is not ('*' or '?'))));

    public static bool TryParse(string? text, IPath paths, out PathPattern? pattern, out string? error)
    {
        ArgumentNullException.ThrowIfNull(paths);
        pattern = null;
        var trimmed = text?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            error = "A path pattern cannot be empty.";
            return false;
        }
        if (trimmed.Length > MaxLength)
        {
            error = $"A path pattern is limited to {MaxLength} characters.";
            return false;
        }
        var absolute = paths.IsFullyQualified(trimmed);
        var normalized = trimmed.Replace('\\', '/');
        var root = absolute ? Root(normalized) : "";
        var segments = normalized[root.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (!absolute && segments.Length > 0 && segments[0] == ".") segments = segments[1..];
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            error = "A path pattern names a path below the project, such as docs/** or src/*.cs, without . or .. parts.";
            return false;
        }
        pattern = new PathPattern(root + string.Join('/', segments), root, segments, absolute, !paths.IsCaseSensitive);
        error = null;
        return true;
    }

    public bool Matches(RuleSubject subject, bool any)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (subject.Value is not PathCall call || call.Targets.Count == 0) return false;
        return any ? call.Targets.Any(Matches) : call.Targets.All(Matches);
    }

    private bool Matches(PathTarget target)
    {
        if (_absolute)
        {
            var normalized = target.Full.Replace('\\', '/');
            var root = Root(normalized);
            return string.Equals(_root, root, _ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                   && Glob(_segments, 0, normalized[root.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries), 0);
        }
        return target.Relative is { } relative && Glob(_segments, 0, relative, 0);
    }

    /// <summary>Segment by segment; <c>**</c> tries every number of segments, which a path's depth bounds.</summary>
    private bool Glob(IReadOnlyList<string> pattern, int p, IReadOnlyList<string> path, int s)
    {
        while (p < pattern.Count)
        {
            if (pattern[p] == "**")
            {
                for (var skip = s; skip <= path.Count; skip++)
                    if (Glob(pattern, p + 1, path, skip)) return true;
                return false;
            }
            if (s >= path.Count || !Segment(pattern[p], path[s])) return false;
            p++;
            s++;
        }
        return s == path.Count;
    }

    private bool Segment(string mask, string segment) => Masks.IsMask(mask)
        ? Masks.Matches(mask, segment, _ignoreCase)
        : string.Equals(mask, segment, _ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary><c>C:/</c>, <c>//server/share/</c> or <c>/</c>: the part of a full path before its first named part.</summary>
    private static string Root(string normalized)
    {
        if (normalized.Length >= 3 && normalized[1] == ':' && normalized[2] == '/') return normalized[..3];
        if (normalized.StartsWith("//", StringComparison.Ordinal))
        {
            var parts = normalized[2..].Split('/', 3);
            return parts.Length >= 2 ? $"//{parts[0]}/{parts[1]}/" : normalized;
        }
        return normalized.StartsWith('/') ? "/" : "";
    }
}
