namespace AI.Search.Benchmarks;

using System.Diagnostics;
using System.Globalization;

/// <summary>
/// The comparison itself: both tools are timed on the same corpus and the same workloads, and one
/// table shows the median of each. Both are measured in a separate process on purpose — ripgrep is
/// one, and the engine is run in a child of its own so the two numbers describe the same thing, a
/// tool doing a search rather than a caller holding a warm runtime.
/// </summary>
/// <remarks>
/// Usage, from the repository root:
/// <code>
/// dotnet run --project benchmarks/AI.Search.Benchmarks -c Release -- compare [--files 20000]
///     [--runs 7] [--rg &lt;path to rg&gt;] [--parallelism 0]
/// </code>
/// The first run generates the corpus, which takes a while and is then reused. Each tool gets one
/// untimed run per workload before the timed ones, so the tree is in the operating system's cache
/// and a comparison is not a measurement of the disk.
/// </remarks>
internal static class ComparisonCommand
{
    /// <summary>The word that selects this command instead of a BenchmarkDotNet run.</summary>
    public const string Name = "compare";

    private const string SelfMeasure = "--self-measure";

    /// <summary>Where the generated corpora live: the chat's temporary directory, so they are not committed.</summary>
    public static string CorpusRoot(int files) =>
        Path.Combine(Path.GetTempPath(), "AI.Search.Benchmarks", "corpus-" + files.ToString(CultureInfo.InvariantCulture));

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Contains(SelfMeasure))
        {
            // The child the comparison starts: one workload, timed inside a process that is already warm.
            return await SelfMeasureAsync(args).ConfigureAwait(false);
        }

        var options = Options.Parse(args);
        var corpus = Corpus.Prepare(CorpusRoot(options.Files), options.Files, options.Regenerate);
        var ripgrep = RipgrepRunner.Locate(options.RipgrepPath);
        var self = Environment.ProcessPath ?? "dotnet";
        var workloads = SearchWorkload.All(corpus.Root);

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Corpus: {corpus.Files} files, {corpus.Bytes / (1024.0 * 1024.0):F1} MB, {corpus.Root}"));
        Console.WriteLine($"Runs per workload: {options.Runs} (median reported)");
        Console.WriteLine($"AI.Search parallelism: {(options.Parallelism == 0 ? Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture) + " (auto)" : options.Parallelism.ToString(CultureInfo.InvariantCulture))}");
        Console.WriteLine(ripgrep is null
            ? "ripgrep: not found — run with --rg <path> or set AI_SEARCH_RG to compare."
            : "ripgrep: " + await ripgrep.VersionAsync().ConfigureAwait(false));
        Console.WriteLine();

        var rows = new List<string>();
        if (ripgrep is not null)
        {
            Console.WriteLine("| Workload               | AI.Search   |         | matches | ripgrep    | ratio |");
            Console.WriteLine("|------------------------|-------------|---------|---------|------------|-------|");
        }

        foreach (var workload in workloads)
        {
            var engine = await MeasureSelfAsync(self, corpus.Root, workload, options).ConfigureAwait(false);
            var baseline = ripgrep is null ? null : await ripgrep.MeasureAsync(workload, options.Runs, CancellationToken.None).ConfigureAwait(false);
            rows.Add(Format(workload, engine, baseline, corpus.Bytes));
            Console.WriteLine(rows[^1]);
        }

        return 0;
    }

    /// <summary>The same command in a child process, so the engine is measured the way ripgrep is.</summary>
    private static async Task<int> SelfMeasureAsync(string[] args)
    {
        var workloadName = Value(args, "--workload") ?? throw new ArgumentException("A self-measure needs a workload.");
        var root = Value(args, "--root") ?? throw new ArgumentException("A self-measure needs a root.");
        var runs = int.Parse(Value(args, "--runs") ?? "1", CultureInfo.InvariantCulture);
        var parallelism = int.Parse(Value(args, "--parallelism") ?? "0", CultureInfo.InvariantCulture);
        var workload = Array.Find(SearchWorkload.All(root), item => item.Name == workloadName)
                       ?? throw new ArgumentException("Unknown workload: " + workloadName);
        var engine = new SearchComposition().Engine;
        var request = workload.Request(root, parallelism);
        await engine.SearchAsync(request).ConfigureAwait(false);

        var samples = new List<double>(runs);
        var matches = 0;
        for (var index = 0; index < runs; index++)
        {
            var startedAt = Stopwatch.GetTimestamp();
            var result = await engine.SearchAsync(request).ConfigureAwait(false);
            samples.Add(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            matches = ResultCount(result);
        }

        var sample = ComparisonSample.Create(samples, matches);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{sample.MedianMilliseconds:F3};{matches}"));
        return 0;
    }

    private static async Task<ComparisonSample> MeasureSelfAsync(
        string executable, string root, SearchWorkload workload, Options options)
    {
        var info = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (!string.Equals(Path.GetFileNameWithoutExtension(executable), "AI.Search.Benchmarks", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The comparison has to run from its own executable to time a child of itself.");
        }

        foreach (var argument in new[]
                 {
                     Name, SelfMeasure, "--workload", workload.Name, "--root", root,
                     "--runs", options.Runs.ToString(CultureInfo.InvariantCulture),
                     "--parallelism", options.Parallelism.ToString(CultureInfo.InvariantCulture)
                 })
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("AI.Search.Benchmarks could not start itself.");
        var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("The measured run failed: " + error);
        }

        var parts = output.Trim().Split(';');
        return new ComparisonSample(options.Runs, double.Parse(parts[0], CultureInfo.InvariantCulture),
            double.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    /// <summary>Matches the engine reported, counted the way the ripgrep line count counts them.</summary>
    private static int ResultCount(SearchResult result) =>
        result.Files.Sum(file => file.Matches.Count > 0 ? file.Matches.Count : file.MatchCount);

    private static string Format(SearchWorkload workload, ComparisonSample engine, ComparisonSample? baseline, long bytes)
    {
        var line = string.Create(CultureInfo.InvariantCulture,
            $"| {workload.Name,-22} | {engine.MedianMilliseconds,9:F1} ms | {engine.MegabytesPerSecond(bytes),7:F0} MB/s | {engine.Matches,7} |");
        if (baseline is null)
        {
            return line + "        — |";
        }

        var ratio = baseline.MedianMilliseconds / engine.MedianMilliseconds;
        return line + string.Create(CultureInfo.InvariantCulture,
            $" {baseline.MedianMilliseconds,7:F1} ms | {ratio,5:F2}x |");
    }

    private static string? Value(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>What the comparison was asked to do.</summary>
    private sealed record Options(int Files, int Runs, string? RipgrepPath, int Parallelism, bool Regenerate)
    {
        public static Options Parse(string[] args) => new(
            int.Parse(Value(args, "--files") ?? "20000", CultureInfo.InvariantCulture),
            Math.Max(int.Parse(Value(args, "--runs") ?? "5", CultureInfo.InvariantCulture), 1),
            Value(args, "--rg"),
            int.Parse(Value(args, "--parallelism") ?? "0", CultureInfo.InvariantCulture),
            args.Contains("--regenerate"));
    }
}