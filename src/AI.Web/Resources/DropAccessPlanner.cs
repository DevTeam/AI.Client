namespace AI.Web.Resources;

using AI.Contracts.Resources;

/// <summary>
/// One grant over the deepest directory that holds every dropped item, subdirectories included.
/// When that would be a whole drive, each item's own directory is granted instead, and an item
/// lying at a root is refused: a drop never opens a whole disk to the assistant.
/// </summary>
/// <remarks>
/// Paths arrive in the host's spelling, while this code may run in the browser, where
/// <see cref="Path"/> knows only '/'. So both separators are handled here, and Windows spellings
/// (a drive letter or a UNC share) compare without case.
/// </remarks>
public sealed class DropAccessPlanner : IDropAccessPlanner
{
    public DropAccessPlan Plan(IReadOnlyList<ResolvedPath> blocked)
    {
        var refused = new List<ResolvedPath>();
        var folders = new List<Location>();
        foreach (var item in blocked)
        {
            if (item.Path is not { } path) continue;
            var location = Location.Parse(path);
            if (item.Kind != ChatResourceKind.Directory) location = location.Parent;
            if (location.IsRoot) refused.Add(item);
            else folders.Add(location);
        }
        if (folders.Count == 0) return new DropAccessPlan([], refused);

        var common = folders.Skip(1).Aggregate((Location?)folders[0], (left, right) => left?.CommonWith(right));
        if (common is { IsRoot: false }) return new DropAccessPlan([common.Value.ToString()], refused);

        // No shared directory below a root: keep the outermost of the items' own directories.
        var distinct = folders.DistinctBy(item => item.Key).ToList();
        var outermost = distinct.Where(item => !distinct.Any(other => other.Key != item.Key && other.Contains(item)));
        return new DropAccessPlan(outermost.Select(item => item.ToString()).ToArray(), refused);
    }

    private readonly record struct Location(string Root, string[] Segments, char Separator, bool IgnoreCase)
    {
        public bool IsRoot => Segments.Length == 0;

        public Location Parent => IsRoot ? this : this with { Segments = Segments[..^1] };

        /// <summary>Compares the way the file system that owns the path does.</summary>
        public string Key
        {
            get
            {
                var text = ToString();
                return IgnoreCase ? text.ToUpperInvariant() : text;
            }
        }

        private StringComparison Comparison => IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        public static Location Parse(string path)
        {
            var windows = path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0])
                || path.StartsWith(@"\\", StringComparison.Ordinal);
            var separator = windows ? '\\' : '/';
            var parts = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            string root;
            IEnumerable<string> segments;
            if (path.StartsWith(@"\\", StringComparison.Ordinal))
            {
                // A UNC share is the smallest thing that can be granted: \\server\share\.
                root = $@"\\{string.Join('\\', parts.Take(2))}\";
                segments = parts.Skip(2);
            }
            else if (windows)
            {
                root = $@"{char.ToUpperInvariant(path[0])}:\";
                segments = parts.Skip(1);
            }
            else
            {
                root = "/";
                segments = parts;
            }
            return new Location(root, segments.ToArray(), separator, windows);
        }

        /// <summary>The shared ancestor, or null when the two lie under different roots.</summary>
        public Location? CommonWith(Location right)
        {
            if (!string.Equals(Root, right.Root, Comparison)) return null;
            var count = 0;
            while (count < Segments.Length && count < right.Segments.Length
                   && string.Equals(Segments[count], right.Segments[count], Comparison)) count++;
            return this with { Segments = Segments[..count] };
        }

        public bool Contains(Location other) =>
            CommonWith(other) is { } common && common.Segments.Length == Segments.Length;

        public override string ToString() => Root + string.Join(Separator, Segments);
    }
}
