namespace AI.Search;

/// <summary>Whether the pattern is plain text or a regular expression.</summary>
public enum PatternKind
{
    /// <summary>The pattern is matched as literal text; the default, as in ripgrep.</summary>
    Literal,

    /// <summary>The pattern is a regular expression.</summary>
    Regex
}

/// <summary>How letter case decides a match, the way ripgrep's <c>-s</c>, <c>-i</c> and <c>-S</c> do.</summary>
public enum CaseMode
{
    /// <summary>
    /// Match case exactly unless the pattern has no uppercase letter, which is ripgrep's <c>-S</c>
    /// and its default: a lowercase pattern matches any case, <c>Needle</c> matches itself only.
    /// </summary>
    Smart,

    /// <summary>Match letter case exactly, as ripgrep's <c>-s</c>.</summary>
    Sensitive,

    /// <summary>Ignore letter case, as ripgrep's <c>-i</c>.</summary>
    Insensitive
}

/// <summary>What a search reports: every matching line, or the files that contain a match.</summary>
public enum MatchOutput
{
    /// <summary>One entry per match, carrying its line number, column and line text.</summary>
    Lines,

    /// <summary>One entry per file, as ripgrep's <c>-l</c>; scanning a file stops at its first match.</summary>
    Files
}

/// <summary>What one search looks for, where it looks and how it reports what it finds.</summary>
/// <remarks>
/// The defaults are ripgrep's: literal, smart case, ignore files respected, hidden entries and
/// binary files skipped, sixteen megabytes per file, no cap on matches, one worker per processor
/// and matching lines as output. <see cref="Path"/> may name a file or a directory; a directory is
/// walked recursively.
/// </remarks>
public sealed record SearchRequest
{
    /// <summary>An absolute file or directory path to search.</summary>
    public required string Path { get; init; }

    /// <summary>Literal text, or a regular expression when <see cref="Kind"/> says so.</summary>
    public required string Pattern { get; init; }

    /// <summary>Whether <see cref="Pattern"/> is text or a regular expression; literal by default.</summary>
    public PatternKind Kind { get; init; }

    /// <summary>How letter case decides a match; smart case by default.</summary>
    public CaseMode Case { get; init; }

    /// <summary>Whether a match must stand as a whole word, as ripgrep's <c>-w</c>.</summary>
    public bool WholeWord { get; init; }

    /// <summary>
    /// Globs a file has to match to be searched, for example <c>*.cs</c> or <c>src/**/*.razor</c>.
    /// Empty means every file. A pattern without a separator is matched against the file name.
    /// </summary>
    public IReadOnlyList<string> Include { get; init; } = [];

    /// <summary>
    /// Globs that leave a file or a whole directory out, for example <c>bin</c> or <c>**/*.min.js</c>.
    /// A matching directory is not descended into.
    /// </summary>
    public IReadOnlyList<string> Exclude { get; init; } = [];

    /// <summary>
    /// Whether <c>.gitignore</c>, <c>.ignore</c> and <c>.rgignore</c> decide what is searched, as
    /// ripgrep does by default. Outside a Git repository the ripgrep-specific files still apply.
    /// </summary>
    public bool RespectIgnoreFiles { get; init; } = true;

    /// <summary>Whether entries whose name starts with a dot are searched; false by default.</summary>
    public bool IncludeHidden { get; init; }

    /// <summary>Whether files that look binary are skipped rather than searched; true by default.</summary>
    public bool SkipBinary { get; init; } = true;

    /// <summary>Largest file that is scanned; a larger one is counted as skipped, as ripgrep does by default.</summary>
    public long MaxFileBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Most matches reported across the whole search; 0, the default, means no cap.</summary>
    public int MaxMatches { get; init; }

    /// <summary>Most matches reported from one file; 0, the default, means no per-file cap.</summary>
    public int MaxMatchesPerFile { get; init; }

    /// <summary>What the result reports per file; matching lines by default.</summary>
    public MatchOutput Output { get; init; }

    /// <summary>How many files are scanned at once; 0, the default, means one per processor.</summary>
    public int Parallelism { get; init; }

    /// <summary>
    /// How long a regular expression may spend on one match attempt before the search fails. Only
    /// the fallback for a pattern the non-backtracking engine cannot run uses the clock at all.
    /// </summary>
    public TimeSpan RegexTimeout { get; init; } = TimeSpan.FromSeconds(2);
}
