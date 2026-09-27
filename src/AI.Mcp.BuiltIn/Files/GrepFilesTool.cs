namespace AI.Mcp.BuiltIn.Files;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.RegularExpressions;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

[McpServerToolType]
public sealed class GrepFilesTool(IPathGuard guard, IBuiltInToolReply reply) : IToolFactory
{
    // Approximate JSON overhead per reported match and per reported file, inflated the same way
    // SearchFilesTool inflates its own — the result is serialized a second time when it is stored
    // as the tool's chat message (see ResultBudget).
    private const int MatchOverheadCharacters = 64;
    private const int FileOverheadCharacters = 64;

    /// <summary>Marks a reported line that was cut down to <see cref="FileLimits.GrepLineCharacters"/>.</summary>
    private const string Ellipsis = "…";

    private const int DefaultMatchesPerFile = 5;

    public McpServerTool Create() => McpServerTool.Create(
        GrepAsync,
        new McpServerToolCreateOptions
        {
            Description = "Search file contents for text and return the matching lines with their line numbers. The path may be a single "
                          + "file or a directory, in which case its tree is searched. 'query' is a literal substring unless isRegex is set, "
                          + "and matching is case-sensitive unless ignoreCase is set. Regular expressions run in non-backtracking mode, "
                          + "which supports the usual syntax but not backreferences or lookaround. One match is reported per line. "
                          + $"At most {FileLimits.GrepMatches} matches are returned, by default {DefaultMatchesPerFile} per file — raise "
                          + "maxMatchesPerFile for an exhaustive list from few files — and the result is capped by total size as well; any "
                          + "cap sets `truncated: true`, while `matchCount` stays the real number of matching lines in that file. Binary "
                          + "files are skipped and counted in `filesSkipped`, and a line much longer than the reported text is returned as "
                          + "a window around the match. By default, version control and build/dependency directories (.git, bin, obj, "
                          + "artifacts, node_modules, .vs, .idea, .vscode) are not descended into; pass excludeDefaults: false to search "
                          + "them too. The path must be absolute and covered by a directory grant with 'read' access."
        });

    [McpServerTool(Name = "grep_files", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(GrepFilesResult))]
    private async Task<CallToolResult> GrepAsync(
        [Description("Absolute path of the file or directory to search.")] [MaxLength(4096)] string path,
        [Description("Text to look for, as a literal substring unless 'isRegex' is set.")] [MaxLength(1024)] string query,
        [Description("Treat 'query' as a regular expression. Backreferences and lookaround are not supported.")]
        bool isRegex = false,
        [Description("Match regardless of letter case.")] bool ignoreCase = false,
        [Description("Glob limiting which files are searched, for example '*.cs' or 'src/**/*.razor'.")] [MaxLength(512)] string? filePattern = null,
        [Description("Glob patterns to skip; a matching directory is not descended into.")] [MaxLength(64)] string[]? excludePatterns = null,
        [Description("Skip version control and build/dependency directories (.git, bin, obj, artifacts, node_modules, .vs, .idea, .vscode) by default.")]
        bool excludeDefaults = true,
        [Description("Lines of surrounding context to return before and after each match.")] [Range(0, 10)] int contextLines = 0,
        [Description("Maximum matching lines reported per file; every match is still counted in 'matchCount'.")] [Range(1, 100)]
        int maxMatchesPerFile = DefaultMatchesPerFile,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(path, query, isRegex, ignoreCase, filePattern, excludePatterns, excludeDefaults,
            contextLines, maxMatchesPerFile, cancellationToken);
        return reply.Reply(result, result.Error is not null);
    }

    private async Task<GrepFilesResult> ExecuteAsync(
        string path,
        string query,
        bool isRegex,
        bool ignoreCase,
        string? filePattern,
        string[]? excludePatterns,
        bool excludeDefaults,
        int contextLines,
        int maxMatchesPerFile,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(query))
        {
            return Failure(path, query, "Query cannot be empty.");
        }

        string resolved;
        try
        {
            resolved = guard.Resolve(path, GrantCapability.Read);
        }
        catch (GrantException error)
        {
            return Failure(path, query, error.Message);
        }

        LineMatcher matcher;
        GlobPattern? include;
        GlobPattern[] excludes;
        try
        {
            matcher = LineMatcher.Create(query, isRegex, ignoreCase);
            include = filePattern is null ? null : GlobPattern.Parse(filePattern);
            excludes = (excludePatterns ?? []).Select(GlobPattern.Parse).ToArray();
        }
        // An unparsable pattern arrives as a RegexParseException (an ArgumentException), while a
        // construct the non-backtracking engine does not implement — a backreference, lookaround —
        // arrives as NotSupportedException. Both are the caller's mistake, not a server fault.
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return Failure(resolved, query, error.Message);
        }

        var single = File.Exists(resolved);
        if (!single && !Directory.Exists(resolved))
        {
            return Failure(resolved, query, "Path does not exist.");
        }

        var scan = new Scan(matcher, contextLines, maxMatchesPerFile);
        try
        {
            if (single)
            {
                await scan.FileAsync(resolved, cancellationToken);
            }
            else
            {
                await WalkAsync(scan, resolved, include, excludes, excludeDefaults, cancellationToken);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return Failure(resolved, query, "The regular expression took too long to evaluate.");
        }

        return scan.ToResult(resolved, query);
    }

    private static async Task WalkAsync(
        Scan scan, string root, GlobPattern? include, GlobPattern[] excludes, bool excludeDefaults, CancellationToken cancellationToken)
    {
        var queue = new Queue<string>();
        queue.Enqueue(root);
        while (queue.Count > 0 && !scan.Stopped)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = queue.Dequeue();
            IEnumerable<FileSystemInfo> children;
            try
            {
                children = new DirectoryInfo(current).EnumerateFileSystemInfos();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var info in children)
            {
                if (scan.Stopped)
                {
                    break;
                }

                var directory = (info.Attributes & FileAttributes.Directory) != 0;
                if (directory && excludeDefaults && FileLimits.DefaultExcludedNames.Contains(info.Name))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(root, info.FullName);
                if (excludes.Any(exclude => exclude.IsMatch(info.Name, relative)))
                {
                    continue;
                }

                if (directory)
                {
                    if (info.LinkTarget is null)
                    {
                        queue.Enqueue(info.FullName);
                    }

                    continue;
                }

                if (include is null || include.IsMatch(info.Name, relative))
                {
                    await scan.FileAsync(info.FullName, cancellationToken);
                }
            }
        }
    }

    private static GrepFilesResult Failure(string path, string query, string message) =>
        new(path, query, [], 0, 0, 0, false, message);

    /// <summary>
    /// Finds the first occurrence of the query in a line. Only the first is needed: the result
    /// reports one <see cref="TextMatch"/> per matching line, not per occurrence.
    /// </summary>
    private sealed class LineMatcher
    {
        private readonly string? _literal;
        private readonly StringComparison _comparison;
        private readonly Regex? _regex;

        private LineMatcher(string? literal, StringComparison comparison, Regex? regex)
        {
            _literal = literal;
            _comparison = comparison;
            _regex = regex;
        }

        public static LineMatcher Create(string query, bool isRegex, bool ignoreCase)
        {
            if (!isRegex)
            {
                return new LineMatcher(query, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal, null);
            }

            // NonBacktracking runs in time linear in the input, so a pathological pattern cannot
            // hang the server the way an ordinary backtracking engine can. The timeout stays as a
            // second line of defence over a very long line; the cost is that backreferences and
            // lookaround are rejected, which the tool's description says.
            var options = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant;
            if (ignoreCase)
            {
                options |= RegexOptions.IgnoreCase;
            }

            return new LineMatcher(null, StringComparison.Ordinal, new Regex(query, options, TimeSpan.FromSeconds(2)));
        }

        /// <summary>Returns the 0-based index and length of the match, or a negative index when the line does not match.</summary>
        public (int Index, int Length) Find(string line)
        {
            if (_literal is not null)
            {
                return (line.IndexOf(_literal, _comparison), _literal.Length);
            }

            var match = _regex!.Match(line);
            return match.Success ? (match.Index, match.Length) : (-1, 0);
        }
    }

    /// <summary>
    /// Accumulates matches across files under the shared caps. Once <see cref="Stopped"/> is set —
    /// the total match cap, the scanned file cap or the size budget is spent — the walk ends and
    /// the result is reported as truncated.
    /// </summary>
    private sealed class Scan(LineMatcher matcher, int contextLines, int maxMatchesPerFile)
    {
        private readonly List<TextFileMatches> _files = [];
        private readonly ResultBudget _budget = new(FileLimits.GrepCharacters);
        private int _scanned;
        private int _skipped;
        private int _total;

        public bool Stopped { get; private set; }

        public async Task FileAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_scanned == FileLimits.GrepFilesScanned)
            {
                Stopped = true;
                return;
            }

            FileStream stream;
            try
            {
                stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536,
                    FileOptions.SequentialScan | FileOptions.Asynchronous);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                _skipped++;
                return;
            }

            await using (stream)
            {
                if (stream.Length > FileLimits.GrepFileBytes || await IsBinaryAsync(stream, cancellationToken))
                {
                    _skipped++;
                    return;
                }

                stream.Position = 0;
                _scanned++;
                try
                {
                    await LinesAsync(path, stream, cancellationToken);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    _skipped++;
                }
            }
        }

        public GrepFilesResult ToResult(string path, string query)
        {
            _files.Sort((left, right) => string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase));
            return new GrepFilesResult(path, query, _files.ToArray(), _scanned, _skipped, _total, Stopped, null);
        }

        private static async Task<bool> IsBinaryAsync(FileStream stream, CancellationToken cancellationToken)
        {
            var length = (int)Math.Min(stream.Length, FileLimits.BinaryProbeBytes);
            if (length == 0)
            {
                return false;
            }

            var probe = new byte[length];
            var read = await stream.ReadAsync(probe, cancellationToken);
            return probe.AsSpan(0, read).Contains((byte)0);
        }

        private async Task LinesAsync(string path, FileStream stream, CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var matches = new List<TextMatch>();
            var pending = new List<Pending>();
            var before = new Queue<string>();
            var matchCount = 0;
            var number = 0;
            var reserved = false;

            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                number++;
                if (!Follow(pending, matches, line))
                {
                    break;
                }

                var (index, length) = matcher.Find(line);
                if (index >= 0)
                {
                    matchCount++;
                    _total++;
                    if (matches.Count + pending.Count < maxMatchesPerFile && _total <= FileLimits.GrepMatches)
                    {
                        if (!reserved && !_budget.TryReserve(path.Length + FileOverheadCharacters))
                        {
                            Stopped = true;
                            break;
                        }

                        reserved = true;
                        var text = Window(line, index, length);
                        var context = before.ToArray();
                        if (!_budget.TryReserve(text.Length + context.Sum(item => item.Length) + MatchOverheadCharacters))
                        {
                            Stopped = true;
                            break;
                        }

                        var match = new Pending(number, index + 1, text, context);
                        if (contextLines == 0)
                        {
                            matches.Add(match.ToMatch());
                        }
                        else
                        {
                            pending.Add(match);
                        }
                    }
                }

                if (contextLines > 0)
                {
                    before.Enqueue(Clip(line));
                    if (before.Count > contextLines)
                    {
                        before.Dequeue();
                    }
                }

                if (_total >= FileLimits.GrepMatches)
                {
                    Stopped = true;
                    break;
                }
            }

            foreach (var match in pending)
            {
                matches.Add(match.ToMatch());
            }

            if (matchCount > 0)
            {
                _files.Add(new TextFileMatches(path, matchCount, matches.ToArray(), matchCount > matches.Count || Stopped));
            }
        }

        /// <summary>
        /// Appends <paramref name="line"/> as an after-context line to every match still waiting
        /// for one, and moves the ones that have collected enough into <paramref name="matches"/>.
        /// Returns false when the budget ran out, which ends the whole scan.
        /// </summary>
        private bool Follow(List<Pending> pending, List<TextMatch> matches, string line)
        {
            if (pending.Count == 0)
            {
                return true;
            }

            var clipped = Clip(line);
            foreach (var match in pending)
            {
                if (!_budget.TryReserve(clipped.Length))
                {
                    Stopped = true;
                    return false;
                }

                match.After.Add(clipped);
            }

            var completed = 0;
            while (completed < pending.Count && pending[completed].After.Count == contextLines)
            {
                matches.Add(pending[completed].ToMatch());
                completed++;
            }

            pending.RemoveRange(0, completed);
            return true;
        }

        private static string Clip(string line) =>
            line.Length <= FileLimits.GrepLineCharacters
                ? line
                : line[..FileLimits.GrepLineCharacters] + Ellipsis;

        /// <summary>
        /// The reported text of a matching line: the line itself when it is short enough, otherwise
        /// a window centred on the match, so that a match far into a minified line is still visible.
        /// The reported column stays relative to the real line.
        /// </summary>
        private static string Window(string line, int index, int length)
        {
            if (line.Length <= FileLimits.GrepLineCharacters)
            {
                return line;
            }

            var room = Math.Max(FileLimits.GrepLineCharacters - Math.Min(length, FileLimits.GrepLineCharacters), 0);
            var start = Math.Clamp(index - (room / 2), 0, Math.Max(line.Length - FileLimits.GrepLineCharacters, 0));
            var end = Math.Min(start + FileLimits.GrepLineCharacters, line.Length);
            return (start > 0 ? Ellipsis : "") + line[start..end] + (end < line.Length ? Ellipsis : "");
        }

        private sealed class Pending(int line, int column, string text, string[] before)
        {
            public List<string> After { get; } = [];

            public TextMatch ToMatch() => new(line, column, text, before, After.ToArray());
        }
    }
}
