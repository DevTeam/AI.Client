namespace AI.Search.Benchmarks;

using BenchmarkDotNet.Attributes;

/// <summary>
/// Measures the engine itself, without a process boundary or a shell in the way. The corpus is
/// generated once per size and reused, so a run measures the search rather than the file system's
/// first contact with a tree.
/// </summary>
/// <remarks>
/// Short runs keep the suite quick enough to execute after every change; the numbers compare
/// revisions of this engine on one machine rather than being absolute timings. Use
/// <c>--job Medium</c> or the <c>compare</c> command for a longer measurement.
/// </remarks>
[MemoryDiagnoser]
[ShortRunJob]
public class EngineBenchmarks
{
    private RipgrepRunner? _ripgrep;
    private ISearchEngine _engine = null!;
    private SearchRequest _request = null!;
    private Corpus _corpus = null!;

    /// <summary>Files in the generated corpus.</summary>
    [Params(2000, 20000)]
    public int Files { get; set; }

    /// <summary>What the search asks for, in the same terms the comparison uses.</summary>
    [Params("literal, every file", "literal, few files", "regex", "files with matches")]
    public string Workload { get; set; } = "literal, every file";

    /// <summary>Workers scanning files; 0 means one per processor.</summary>
    [Params(1, 0)]
    public int Parallelism { get; set; }

    /// <summary>The version of the binary the plot column refers to, when one was found.</summary>
    public string Ripgrep => _ripgrep is null ? "not found" : System.IO.Path.GetFileName(_ripgrep.Executable);

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _corpus = Corpus.Prepare(ComparisonCommand.CorpusRoot(Files), Files, regenerate: false);
        _request = Array.Find(SearchWorkload.All(_corpus.Root), workload => string.Equals(workload.Name, Workload, StringComparison.Ordinal))!.Request(_corpus.Root, Parallelism);
        _engine = new SearchComposition().Engine;

        // One untimed run, so the measured ones find the tree in the operating system's cache, which
        // is what a repeated search of a project looks like.
        await _engine.SearchAsync(_request).ConfigureAwait(false);
        _ripgrep = RipgrepRunner.Locate(null);
    }

    [Benchmark]
    public Task<SearchResult> Search() => _engine.SearchAsync(_request);
}