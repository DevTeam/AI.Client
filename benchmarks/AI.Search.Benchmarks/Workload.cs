namespace AI.Search.Benchmarks;

/// <summary>
/// One task both tools run: what to look for and how to report it, in this engine's terms and in
/// the arguments <c>rg</c> takes for the same thing. Keeping the two descriptions next to each other
/// is what makes the comparison about implementations rather than about two different questions.
/// </summary>
internal sealed record SearchWorkload(string Name, string Pattern, PatternKind Kind, MatchOutput Output, string[] RipgrepArgs)
{
    /// <summary>The workloads the comparison runs, in the order it reports them.</summary>
    public static SearchWorkload[] All(string root) =>
    [
        new("literal, every file", Corpus.CommonKeyword, PatternKind.Literal, MatchOutput.Lines,
            Args(root, Corpus.CommonKeyword, "--line-number")),
        new("literal, few files", Corpus.RareKeyword, PatternKind.Literal, MatchOutput.Lines,
            Args(root, Corpus.RareKeyword, "--line-number")),
        new("literal, ignore case", "sharedkeyword", PatternKind.Literal, MatchOutput.Lines,
            ["--ignore-case", .. Args(root, "sharedkeyword", "--line-number")]),
        new("regex", @"public\s+sealed\s+class\s+Generated\d+_3", PatternKind.Regex, MatchOutput.Lines,
            Args(root, @"public\s+sealed\s+class\s+Generated\d+_3", "--line-number")),
        new("word", Corpus.CommonKeyword, PatternKind.Literal, MatchOutput.Lines,
            Args(root, Corpus.CommonKeyword, "--line-number", "--word-regexp")),
        new("files with matches", Corpus.CommonKeyword, PatternKind.Literal, MatchOutput.Files,
            Args(root, Corpus.CommonKeyword, "--files-with-matches")),
    ];

    /// <summary>The engine's request for this workload.</summary>
    public SearchRequest Request(string root, int parallelism) => new()
    {
        Path = root,
        Pattern = Pattern,
        Kind = Kind,
        Case = Name.Contains("ignore case", StringComparison.Ordinal) ? CaseMode.Insensitive : CaseMode.Smart,
        WholeWord = RipgrepArgs.Contains("--word-regexp"),
        Output = Output,
        Parallelism = parallelism
    };

    /// <summary>
    /// The arguments a person would type; <c>--no-config</c> keeps a ripgreprc on the machine from
    /// changing what is measured, and the ignore rules stay on, as they are in the engine's defaults.
    /// </summary>
    private static string[] Args(string root, string pattern, params string[] options) =>
        ["--no-config", "--no-heading", "--color", "never", .. options, "--", pattern, root];
}