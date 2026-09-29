namespace AI.Web.Resources;

using System.Runtime.CompilerServices;
using System.Text;
using AI.Contracts.Workspace;

/// <summary>
/// Parses what <c>GitWorkspaceDiffReader.ReadDiff</c> wrote: <c>git diff HEAD --relative</c>, then
/// an "Untracked files" list, and a "[cut: …]" line where it stopped at its cap.
/// </summary>
/// <remarks>
/// A user message is drawn on every render of the transcript, so a parsed excerpt is kept for as
/// long as the excerpt string itself lives; a diff can run to a hundred kilobytes.
/// </remarks>
public sealed class DiffSnapshotReader : IDiffSnapshotReader
{
    private const string UntrackedHeader = "Untracked files (not in the diff):";
    private readonly ConditionalWeakTable<string, DiffSnapshot> _cache = new();

    public DiffSnapshot? Read(string? excerpt, string root)
    {
        if (string.IsNullOrEmpty(excerpt)) return null;
        if (_cache.TryGetValue(excerpt, out var cached)) return cached;
        var snapshot = Parse(excerpt, root);
        _cache.AddOrUpdate(excerpt, snapshot);
        return snapshot;
    }

    private static DiffSnapshot Parse(string excerpt, string root)
    {
        var separator = root.Contains('\\', StringComparison.Ordinal) ? '\\' : '/';
        var prefix = root.TrimEnd('\\', '/') + separator;
        string Absolute(string relative) => prefix + relative.Replace('/', separator);

        var files = new List<FileChange>();
        var cut = false;
        var untracked = false;
        FileBuilder? current = null;

        void Flush()
        {
            if (current is { } file) files.Add(file.Build(Absolute));
            current = null;
        }

        foreach (var line in excerpt.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.StartsWith("[cut:", StringComparison.Ordinal))
            {
                cut = true;
                break;
            }
            if (line == UntrackedHeader)
            {
                Flush();
                untracked = true;
                continue;
            }
            if (untracked)
            {
                var name = line.Trim();
                if (name.StartsWith("... and ", StringComparison.Ordinal)) cut = true;
                else if (name.Length > 0) files.Add(new FileChange(Absolute(name), FileChangeKind.Added, null, null));
                continue;
            }
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                Flush();
                current = new FileBuilder(HeaderPath(line));
                continue;
            }
            current?.Add(line);
        }
        Flush();
        var additions = files.Sum(file => file.Additions ?? 0);
        var deletions = files.Sum(file => file.Deletions ?? 0);
        return new DiffSnapshot(new WorkspaceChangeSet(files, additions, deletions), cut);
    }

    /// <summary>"diff --git a/x b/x": the new side, which "+++" or "rename to" may still correct.</summary>
    private static string HeaderPath(string line)
    {
        var at = line.LastIndexOf(" b/", StringComparison.Ordinal);
        return at < 0 ? line["diff --git ".Length..] : line[(at + 3)..];
    }

    private sealed class FileBuilder(string path)
    {
        private readonly StringBuilder _diff = new();
        private string _path = path;
        private string? _previousPath;
        private FileChangeKind _kind = FileChangeKind.Modified;
        private bool _binary;
        private bool _inHunks;
        private int _additions;
        private int _deletions;

        public void Add(string line)
        {
            // Before the first hunk a "---" or "+++" line is a header; inside one it is a changed line.
            if (!_inHunks)
            {
                if (line.StartsWith("@@", StringComparison.Ordinal)) _inHunks = true;
                else
                {
                    Header(line);
                    return;
                }
            }
            if (line.Length == 0) return;
            if (line[0] == '+') _additions++;
            else if (line[0] == '-') _deletions++;
            _diff.Append(line).Append('\n');
        }

        private void Header(string line)
        {
            if (line.StartsWith("new file mode", StringComparison.Ordinal)) _kind = FileChangeKind.Added;
            else if (line.StartsWith("deleted file mode", StringComparison.Ordinal)) _kind = FileChangeKind.Deleted;
            else if (line.StartsWith("rename from ", StringComparison.Ordinal))
            {
                _previousPath = line["rename from ".Length..];
                _kind = FileChangeKind.Renamed;
            }
            else if (line.StartsWith("rename to ", StringComparison.Ordinal)) _path = line["rename to ".Length..];
            else if (line.StartsWith("+++ b/", StringComparison.Ordinal)) _path = line["+++ b/".Length..];
            else if (line.StartsWith("Binary files ", StringComparison.Ordinal)) _binary = true;
        }

        public FileChange Build(Func<string, string> absolute) => new(absolute(_path), _kind,
            _binary ? null : _additions, _binary ? null : _deletions,
            _previousPath is { } previous ? absolute(previous) : null,
            _diff.Length == 0 ? null : _diff.ToString(), _binary);
    }
}
