namespace AI.Search.Scanning;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using AI.Contracts.FileSystem;
using AI.Search.Matchers;
using AI.Search.Walking;

/// <summary>
/// Runs a search the way ripgrep does: one walk feeding a bounded queue, and a pool of workers that
/// read files from it concurrently. The walk is I/O bound on directory listings while the workers
/// are CPU bound on matching, so the two overlap instead of taking turns.
/// </summary>
/// <remarks>
/// The match cap ends the search rather than merely cropping its output: once the budget is spent
/// the workers stop taking files and the walk stops producing them, which is what makes a capped
/// search cheap. The work already in flight is completed and reported, so the result is a prefix of
/// the complete one and says so through <see cref="SearchResult.Truncated"/>.
/// </remarks>
internal sealed class SearchEngine(SearchPlanFactory plans, FileWalker walker, FileScanner scanner, IPath paths) : ISearchEngine
{
    private readonly SearchPlanFactory _plans = plans;
    private readonly FileWalker _walker = walker;
    private readonly FileScanner _scanner = scanner;
    private readonly IPath _paths = paths;

    public async Task<SearchResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var plan = await _plans.CreateAsync(request, cancellationToken);

        // Compiled before the walk starts, so an invalid pattern fails before any file is opened.
        var matcher = LineMatchers.Create(request);

        var state = new SearchState(plan);
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        state.Stop = stopped;
        try
        {
            if (plan.IsFile)
            {
                await ScanSingleAsync(plan, matcher, state, cancellationToken);
            }
            else
            {
                await ScanTreeAsync(plan, matcher, state, stopped.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && state.LimitReached)
        {
            // The match cap ended the search early. That is a truncated result, not a failure: the
            // caller learns what was found before the cap and that more may exist.
        }

        var comparison = _paths.Comparison == StringComparison.OrdinalIgnoreCase
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var files = state.Files.OrderBy(file => file.Path, comparison).ToArray();
        var totals = new SearchTotals(state.Searched, state.Skipped, files.Length, state.Bytes, state.Matches);
        return new SearchResult(plan.Root, request.Pattern, files, totals, state.LimitReached, Stopwatch.GetElapsedTime(startedAt));
    }

    private async Task ScanSingleAsync(SearchPlan plan, ILineMatcher matcher, SearchState state, CancellationToken cancellationToken)
    {
        var scan = new ContentScanner(matcher);
        var matches = new List<SearchMatch>();
        var name = _paths.GetFileName(plan.Root);
        var result = await _scanner.ScanAsync(plan.Root, name, plan, scan, plan.MaxMatchesPerFile, matches, cancellationToken);
        state.Record(result);
    }

    private async Task ScanTreeAsync(SearchPlan plan, ILineMatcher matcher, SearchState state, CancellationToken stopped)
    {
        // Bounded, so a fast walk over a shallow tree cannot queue an unbounded number of paths
        // while a slow disk keeps the workers behind.
        var options = new BoundedChannelOptions(Math.Max(plan.Parallelism * 4, 16))
        {
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        };
        var channel = Channel.CreateBounded<SearchCandidate>(options);
        var walking = _walker.WalkAsync(plan, channel.Writer, stopped);

        var workers = new Task[plan.Parallelism];
        for (var index = 0; index < workers.Length; index++)
        {
            // Each worker owns its decoder buffer, so the pattern is compiled once and the buffer is
            // reused by one thread instead of being contended by all of them.
            workers[index] = WorkAsync(plan, new ContentScanner(matcher), channel.Reader, state, stopped);
        }

        try
        {
            await walking;
        }
        finally
        {
            // The workers end on their own: the walk completed the channel, or the cap stopped it.
            await Task.WhenAll(workers);
        }
    }

    private async Task WorkAsync(
        SearchPlan plan,
        ContentScanner scan,
        ChannelReader<SearchCandidate> reader,
        SearchState state,
        CancellationToken stopped)
    {
        var matches = new List<SearchMatch>();
        try
        {
            while (await reader.WaitToReadAsync(stopped))
            {
                while (reader.TryRead(out var candidate))
                {
                    if (!state.TryReserve())
                    {
                        return;
                    }

                    matches.Clear();
                    var result = await _scanner.ScanAsync(candidate.Path, candidate.RelativePath, plan, scan,
                        state.FileLimit, matches, stopped);
                    state.Record(result);
                }
            }
        }
        catch (OperationCanceledException) when (stopped.IsCancellationRequested)
        {
            // The match cap or the caller's cancellation ended the work; the result is assembled
            // from what was recorded before that.
        }
    }

    /// <summary>
    /// Everything the workers share: the totals, the reported files and the match budget. The
    /// counters are atomic because every worker updates them, and reaching the cap cancels the
    /// linked token, which is how the walk and the remaining workers stop promptly.
    /// </summary>
    private sealed class SearchState(SearchPlan plan)
    {
        private readonly ConcurrentBag<FoundFile> _files = new();
        private int _searched;
        private int _skipped;
        private int _matches;
        private long _bytes;

        public CancellationTokenSource? Stop { get; set; }

        public bool LimitReached { get; private set; }

        public int Searched => Volatile.Read(ref _searched);

        public int Skipped => Volatile.Read(ref _skipped);

        public int Matches => Volatile.Read(ref _matches);

        public long Bytes => Interlocked.Read(ref _bytes);

        public IReadOnlyCollection<FoundFile> Files => _files;

        /// <summary>Whether another file may be scanned: false once the match cap is spent.</summary>
        public bool TryReserve()
        {
            if (plan.MaxMatches == 0 || Volatile.Read(ref _matches) < plan.MaxMatches)
            {
                return true;
            }

            MarkLimit();
            return false;
        }

        /// <summary>Matches to report from the next file, within the per-file limit and the remaining budget.</summary>
        public int FileLimit
        {
            get
            {
                var perFile = plan.MaxMatchesPerFile;
                if (plan.MaxMatches == 0)
                {
                    return perFile;
                }

                var remaining = Math.Max(plan.MaxMatches - Volatile.Read(ref _matches), 0);
                return perFile == 0 ? remaining : Math.Min(perFile, remaining);
            }
        }

        public void Record(FileScanResult result)
        {
            switch (result.Outcome)
            {
                case FileOutcome.Skipped:
                    Interlocked.Increment(ref _skipped);
                    return;
                case FileOutcome.NoMatch:
                    Interlocked.Increment(ref _searched);
                    Interlocked.Add(ref _bytes, result.Bytes);
                    return;
                default:
                    Interlocked.Increment(ref _searched);
                    Interlocked.Add(ref _bytes, result.Bytes);
                    Interlocked.Add(ref _matches, result.File!.MatchCount);
                    _files.Add(result.File);
                    break;
            }

            if (plan.MaxMatches > 0 && Volatile.Read(ref _matches) >= plan.MaxMatches)
            {
                MarkLimit();
            }
        }

        private void MarkLimit()
        {
            LimitReached = true;
            Stop?.Cancel();
        }
    }
}
