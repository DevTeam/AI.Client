namespace AI.Mcp.BuiltIn.Files;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.RegularExpressions;
using AI.Contracts.FileSystem;
using AI.Search;
using Grants;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

/// <summary>
/// The <c>grep_files</c> tool: the caller's question is put to <see cref="ISearchEngine"/>, and its
/// answer is turned into what one tool call may return.
/// </summary>
/// <remarks>
/// The engine walks the tree, honours the ignore files and matches the files; this tool decides what
/// the engine is asked and what of its answer is worth sending. There is therefore one
/// implementation of a recursive search in the product, and a fix or a speed-up in it is a fix in
/// the tool.
/// <para>
/// Two things the engine does not do belong here: the caps that keep one result bounded in size,
/// which cut the answer down rather than ending the search, and the context lines, which the engine
/// does not report and which are read around the lines it did report.
/// </para>
/// </remarks>
[McpServerToolType]
public sealed class GrepFilesTool(IPathGuard guard, IBuiltInToolReply reply, ISearchEngine engine, IFileSystem files, IPath paths) : IToolFactory
{
    /// <summary>Marks a context line that was cut down to <see cref="FileLimits.GrepLineCharacters"/>.</summary>
    private const string Ellipsis = "…";

    private const int DefaultMatchesPerFile = 5;

    /// <summary>Approximate JSON cost of one reported match and of one reported file.</summary>
    private const int MatchOverheadCharacters = 64;
    private const int FileOverheadCharacters = 64;

    /// <summary>One matching line with the lines around it, when the caller asked for them.</summary>
    private sealed record Reported(SearchMatch Match, string[]? Before, string[]? After);

    public McpServerTool Create() => McpServerTool.Create(
        GrepAsync,
        new McpServerToolCreateOptions
        {
            Description = "Search file contents for text and return the matching lines with their line numbers. The path may be a single "
                          + "file or a directory, in which case its tree is searched. 'query' is a literal substring unless isRegex is set, "
                          + "and matching is case-sensitive unless ignoreCase is set. Regular expressions run in non-backtracking mode, "
                          + "which supports the usual syntax but not backreferences or lookaround; a pattern that needs them is matched by "
                          + "the ordinary engine with a timeout instead. One match is reported per line. "
                          + $"At most {FileLimits.GrepMatches} matches are returned, by default {DefaultMatchesPerFile} per file — raise "
                          + "maxMatchesPerFile for an exhaustive list from few files — and the result is capped by total size as well; any "
                          + "cap sets `truncated: true`, while `matchCount` stays the real number of matching lines in that file. Binary "
                          + "files are skipped and counted in `filesSkipped`, and a line much longer than the reported text is returned as "
                          + "a window around the match. File paths are relative to `path`. By default, version control and "
                          + "build/dependency directories (.git, bin, obj, artifacts, node_modules, .vs, .idea, .vscode) and whatever the "
                          + "Git repository's ignore files ignore are skipped; pass excludeDefaults: false to search them too. The path must "
                          + "be absolute and covered by a directory grant with 'read' access."
        });

    [McpServerTool(Name = "grep_files", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(GrepFilesResult))]
    private async Task<CallToolResult> GrepAsync(
        [Description("Absolute path of the file or directory to search.")] [MaxLength(4096)] string path,
        [Description("Text to look for, as a literal substring unless 'isRegex' is set.")] [MaxLength(1024)] string query,
        [Description("Treat 'query' as a regular expression. Backreferences and lookaround are supported only by the slower path.")]
        bool isRegex = false,
        [Description("Match regardless of letter case.")] bool ignoreCase = false,
        [Description("Glob limiting which files are searched, for example '*.cs' or 'src/**/*.razor'.")] [MaxLength(512)] string? filePattern = null,
        [Description("Glob patterns to skip; a matching directory is not descended into.")] [MaxLength(64)] string[]? excludePatterns = null,
        [Description("Skip version control and build/dependency directories (.git, bin, obj, artifacts, node_modules, .vs, .idea, .vscode) and Git-ignored entries by default.")]
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

    /// <summary>
    /// The search behind the tool, without the MCP envelope: a test calls this directly, and the
    /// tool itself only turns its answer into a reply.
    /// </summary>
    internal async Task<GrepFilesResult> ExecuteAsync(
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

        var request = new SearchRequest
        {
            Path = resolved,
            Pattern = query,
            Kind = isRegex ? PatternKind.Regex : PatternKind.Literal,
            // 'ignoreCase' off means the comparison is exact, which is the engine's sensitive mode and
            // not its smart case: a lowercase query stays a lowercase query.
            Case = ignoreCase ? CaseMode.Insensitive : CaseMode.Sensitive,
            Include = string.IsNullOrWhiteSpace(filePattern) ? [] : [filePattern],
            // An exclusion glob leaves out a directory together with its subtree, and a bare name such
            // as 'bin' means an entry of that name at any depth — both are what excludePatterns means.
            Exclude = Excludes(excludePatterns, excludeDefaults),
            // With excludeDefaults off the caller asked to see everything: version control metadata and
            // hidden entries included, exactly as the tool describes.
            RespectIgnoreFiles = excludeDefaults,
            IncludeHidden = !excludeDefaults,
            SkipBinary = true,
            MaxFileBytes = FileLimits.GrepFileBytes,
            MaxMatches = FileLimits.GrepMatches,
            MaxMatchesPerFile = maxMatchesPerFile,
            Output = MatchOutput.Lines
        };

        // A single file is its own root, and the engine reports it by file name; a directory is walked
        // and its entries are named relative to it, which keeps one absolute prefix out of the result.
        var single = await files.FileExistsAsync(resolved, cancellationToken);
        SearchResult search;
        try
        {
            search = await engine.SearchAsync(request, cancellationToken);
        }
        // An unparsable pattern and one the engine cannot run are both the caller's mistake rather
        // than a server fault, and so is a path that has gone since it was resolved.
        catch (Exception error) when (error is ArgumentException or NotSupportedException or DirectoryNotFoundException)
        {
            return Failure(resolved, query, error.Message);
        }
        catch (RegexMatchTimeoutException)
        {
            return Failure(resolved, query, "The regular expression took too long to evaluate.");
        }

        return await PresentAsync(resolved, query, search, contextLines, single, cancellationToken);
    }

    /// <summary>
    /// Turns what the engine found into the result the tool promises: the reported lines within the
    /// character budget, with their context lines when they were asked for.
    /// </summary>
    private async Task<GrepFilesResult> PresentAsync(
        string root, string query, SearchResult search, int contextLines, bool single, CancellationToken cancellationToken)
    {
        var budget = new ResultBudget(FileLimits.GrepCharacters);
        var files = new List<TextFileMatches>(search.Files.Count);
        var total = 0;
        var truncated = search.Truncated;

        foreach (var file in search.Files)
        {
            // The name costs the budget too: a tree whose paths are all long is as much a threat to
            // the result size as the text of its lines.
            if (!budget.TryReserve(file.Path.Length + FileOverheadCharacters))
            {
                truncated = true;
                break;
            }

            var reported = contextLines == 0
                ? file.Matches.Select(match => new Reported(match, null, null)).ToArray()
                : await WithContextAsync(single ? root : paths.Combine(root, file.Path), file, contextLines, cancellationToken);

            var matches = new List<TextMatch>(reported.Length);
            foreach (var match in reported)
            {
                var cost = match.Match.Text.Length + ContextLength(match) + MatchOverheadCharacters;
                if (!budget.TryReserve(cost))
                {
                    truncated = true;
                    break;
                }

                matches.Add(new TextMatch(match.Match.Line, match.Match.Column, match.Match.Text, match.Before, match.After));
            }

            // A file is cut when the result carries fewer lines than the file holds; the engine counts
            // every matching line even when it reported only the first ones.
            var fileTruncated = file.MatchCount > matches.Count;
            truncated |= fileTruncated;
            files.Add(new TextFileMatches(file.Path, Math.Max(file.MatchCount, matches.Count), matches.ToArray(), fileTruncated));
            total += matches.Count;
        }

        return new GrepFilesResult(root, query, files.ToArray(), search.Totals.FilesSearched,
            search.Totals.FilesSkipped, total, truncated, null);
    }

    /// <summary>
    /// The matches of one file with the lines around them. The file is read only for this: the engine
    /// already decided which lines match, so nothing is matched a second time.
    /// </summary>
    private async Task<Reported[]> WithContextAsync(string path, FoundFile file, int contextLines, CancellationToken cancellationToken)
    {
        if (file.Matches.Count == 0)
        {
            return [];
        }

        // A file that cannot be read a second time keeps its matches without context: the search
        // found them, and losing the surrounding lines is a smaller loss than losing the matches.
        var lines = await ReadLinesAsync(path, file.Matches[^1].Line + contextLines, cancellationToken);
        if (lines.Count == 0)
        {
            return file.Matches.Select(match => new Reported(match, null, null)).ToArray();
        }

        var reported = new Reported[file.Matches.Count];
        for (var index = 0; index < file.Matches.Count; index++)
        {
            var match = file.Matches[index];
            reported[index] = new Reported(match,
                Context(lines, match.Line - contextLines, match.Line - 1),
                Context(lines, match.Line + 1, match.Line + contextLines));
        }

        return reported;
    }

    /// <summary>The lines of the file up to <paramref name="lastLine"/>, or none when it cannot be read.</summary>
    private async Task<IReadOnlyList<string>> ReadLinesAsync(string path, int lastLine, CancellationToken cancellationToken)
    {
        var options = new FileReadOptions(
            Share: FileShare.ReadWrite | FileShare.Delete,
            BufferSize: 65536,
            Options: FileOptions.SequentialScan | FileOptions.Asynchronous);
        Stream stream;
        try
        {
            stream = await files.OpenReadAsync(path, options, cancellationToken)
                     ?? throw new FileNotFoundException($"File does not exist: {path}", path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return [];
        }

        await using (stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var lines = new List<string>(Math.Max(lastLine, 1));
            try
            {
                while (lines.Count < lastLine && await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    lines.Add(Clip(line));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return [];
            }

            return lines;
        }
    }

    /// <summary>
    /// The context lines between two 1-based line numbers, both included and both clipped. A range
    /// that falls outside the file — at its first or last line — is shorter rather than padded.
    /// </summary>
    private static string[]? Context(IReadOnlyList<string> lines, int from, int to)
    {
        var start = Math.Max(from, 1);
        var end = Math.Min(to, lines.Count);
        if (end < start)
        {
            return null;
        }

        var context = new string[end - start + 1];
        for (var index = 0; index < context.Length; index++)
        {
            context[index] = lines[start - 1 + index];
        }

        return context;
    }

    private static int ContextLength(Reported match) =>
        (match.Before?.Sum(line => line.Length) ?? 0) + (match.After?.Sum(line => line.Length) ?? 0);

    private static string[] Excludes(string[]? patterns, bool excludeDefaults)
    {
        var excludes = new List<string>(patterns ?? []);
        if (excludeDefaults)
        {
            excludes.AddRange(FileLimits.DefaultExcludedNames);
        }

        return excludes.ToArray();
    }

    private static string Clip(string line) =>
        line.Length <= FileLimits.GrepLineCharacters
            ? line
            : line[..FileLimits.GrepLineCharacters] + Ellipsis;

    private static GrepFilesResult Failure(string path, string query, string message) =>
        new(path, query, [], 0, 0, 0, false, message);
}
