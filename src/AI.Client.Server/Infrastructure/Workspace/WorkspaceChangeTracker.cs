namespace AI.Client.Infrastructure.Workspace;

using System.Collections.Concurrent;
using System.Text.Json;
using Application.Tools;
using Application.Workspace;
using Contracts.Tools;
using Contracts.Workspace;

/// <summary>
/// Observes the built-in mutating tools and turns them into a net, per-run list of changed files.
/// </summary>
public sealed class WorkspaceChangeTracker(ILineDiff diff) : IWorkspaceChangeTracker
{
    /// <summary>Built-in tools whose arguments name a path they are about to modify.</summary>
    private static readonly Dictionary<string, string[]> MutatingPathArguments = new(StringComparer.Ordinal)
    {
        ["write_file"] = ["path"],
        ["edit_file"] = ["path"],
        ["create_directory"] = ["path"],
        ["move_file"] = ["source", "destination"],
        ["delete_file"] = ["path"],
        ["delete_directory"] = ["path"],
    };

    /// <summary>Past this size a file is compared by existence only; reading it twice is not worth it.</summary>
    private const long MaxTrackedBytes = 8 * 1024 * 1024;

    private readonly ILineDiff _diff = diff;
    private readonly ConcurrentDictionary<WorkspaceRunKey, RunState> _runs = new();

    public Task BeginRunAsync(WorkspaceRunKey run, IReadOnlyList<ToolDirectoryGrant> grants, WorkspaceRunKey? parent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);
        // A run cannot be its own parent, and a parent that is not being tracked is no parent at
        // all: either would turn the walk below into a loop or a dead end.
        var linked = parent is { } above && above != run && _runs.ContainsKey(above) ? above : (WorkspaceRunKey?)null;
        _runs[run] = new RunState(grants.Select(grant => grant.Root).ToArray(), linked);
        return Task.CompletedTask;
    }

    public Task RecordIntentAsync(WorkspaceRunKey run, ToolDescriptor tool, string arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (!_runs.TryGetValue(run, out var state)) return Task.CompletedTask;
        foreach (var path in PathsFor(tool, arguments)) state.CaptureBaseline(path);
        return Task.CompletedTask;
    }

    public Task RecordEffectAsync(
        WorkspaceRunKey run, ToolDescriptor tool, string arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (!_runs.TryGetValue(run, out var state)) return Task.CompletedTask;

        // A failed call may still have written something, so the paths stay tracked either way;
        // the comparison below simply finds no difference when nothing happened.
        foreach (var path in PathsFor(tool, arguments)) state.CaptureBaseline(path);
        return Task.CompletedTask;
    }

    public Task<WorkspaceChangeSet> SnapshotAsync(WorkspaceRunKey run, CancellationToken cancellationToken)
    {
        if (!_runs.TryGetValue(run, out var state)) return Task.FromResult(WorkspaceChangeSet.Empty);

        // A path both the run and one of its subtasks touched is one file with one net difference,
        // not two rows, so the earliest baseline wins: measuring against the later one would credit
        // the turn with only the tail of its own change.
        var merged = new Dictionary<string, Baseline>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, baseline) in state.Baselines().Concat(Descendants(run).SelectMany(child => child.Baselines())))
            if (!merged.TryGetValue(path, out var held) || baseline.Order < held.Order)
                merged[path] = baseline;
        return Task.FromResult(RunState.Compose(_diff, merged));
    }

    public Task CompleteRunAsync(WorkspaceRunKey run, CancellationToken cancellationToken)
    {
        if (!_runs.TryRemove(run, out var state)) return Task.CompletedTask;

        // The parent outlives its subtasks by definition, and its total must keep covering them
        // once they are gone. Anything the parent already tracks for the same path stays: it was
        // captured first, so it is the older of the two.
        if (state.Parent is { } parent && _runs.TryGetValue(parent, out var above)) above.Absorb(state.Baselines());
        return Task.CompletedTask;
    }

    /// <summary>
    /// Every run delegated from <paramref name="run"/>, however deeply. Walked from the children
    /// rather than held as a list on the parent, so a subtask that has already finished and handed
    /// its baselines up is simply absent instead of leaving a dangling entry behind.
    /// </summary>
    private IEnumerable<RunState> Descendants(WorkspaceRunKey run)
    {
        var frontier = new Queue<WorkspaceRunKey>([run]);
        var seen = new HashSet<WorkspaceRunKey> { run };
        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var (key, state) in _runs)
            {
                if (state.Parent != current || !seen.Add(key)) continue;
                frontier.Enqueue(key);
                yield return state;
            }
        }
    }

    private static IEnumerable<string> PathsFor(ToolDescriptor tool, string arguments)
    {
        var reference = ToolRef.Parse(tool.Name);
        if (!reference.IsBuiltIn || !MutatingPathArguments.TryGetValue(reference.Name, out var properties)) yield break;
        if (ToolPresentations.ParseArguments(arguments) is not { ValueKind: JsonValueKind.Object } input) yield break;
        foreach (var property in properties)
            if (input.TryGetProperty(property, out var value)
                && value.ValueKind == JsonValueKind.String
                && value.GetString() is { Length: > 0 } path)
                yield return path;
    }

    private sealed class RunState(IReadOnlyList<string> grantRoots, WorkspaceRunKey? parent)
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, Baseline> _baselines = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The run that delegated this one, if any. Fixed at the run's start.</summary>
        public WorkspaceRunKey? Parent => parent;

        /// <summary>The baselines held right now, safe to read while the run is still going.</summary>
        public IReadOnlyList<KeyValuePair<string, Baseline>> Baselines()
        {
            lock (_gate) return [.. _baselines];
        }

        /// <summary>
        /// Takes over the baselines of a finished subtask. A path this run already tracks keeps the
        /// baseline it has: this run started first, so its copy is the older one.
        /// </summary>
        public void Absorb(IReadOnlyList<KeyValuePair<string, Baseline>> inherited)
        {
            lock (_gate)
                foreach (var (path, baseline) in inherited)
                    if (!_baselines.TryGetValue(path, out var held) || baseline.Order < held.Order)
                        _baselines[path] = baseline;
        }

        /// <summary>
        /// Remembers a path's content the first time the run is about to touch it. Later calls are
        /// ignored on purpose: the baseline must stay the state at the run's start, which is what
        /// makes repeated edits collapse into one net difference and keeps changes the user made
        /// beforehand out of the result.
        /// </summary>
        public void CaptureBaseline(string path)
        {
            if (Canonical(path) is not { } canonical) return;
            lock (_gate)
            {
                if (_baselines.ContainsKey(canonical)) return;
                _baselines[canonical] = Read(canonical);
            }
        }

        /// <summary>
        /// Turns baselines into the net change set, reading each path as it stands now. Static
        /// because the set a caller is shown may span several runs: its own and its subtasks'.
        /// </summary>
        public static WorkspaceChangeSet Compose(ILineDiff diff, IReadOnlyDictionary<string, Baseline> tracked)
        {
            var byPath = new Dictionary<string, FileChange>(StringComparer.OrdinalIgnoreCase);
            foreach (var (path, baseline) in tracked)
            {
                var current = Read(path);
                if (Describe(diff, path, baseline, current) is not { } change) continue;
                byPath[path] = change;
            }

            var files = byPath.Values.ToList();
            files.Sort((left, right) => string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase));
            return new WorkspaceChangeSet(files, files.Sum(file => file.Additions ?? 0),
                files.Sum(file => file.Deletions ?? 0));
        }

        private static FileChange? Describe(ILineDiff diff, string path, Baseline before, Baseline after)
        {
            if (!before.Exists && !after.Exists) return null;

            if (before.Exists && !after.Exists)
                return new FileChange(path, FileChangeKind.Deleted, 0, before.Text is { } text ? Lines(text) : null,
                    IsBinary: before.Text is null,
                    Confidence: before.Text is null ? FileChangeConfidence.Approximate : FileChangeConfidence.Measured);

            if (!before.Exists && after.Exists)
            {
                // A created file is a comparison against nothing, not a case without one: every
                // line is an addition, and the run is the only chance to show them, so the diff is
                // built here rather than left null for the card to explain away.
                if (after.Text is not { } added)
                    return new FileChange(path, FileChangeKind.Added, null, 0,
                        IsBinary: true, Confidence: FileChangeConfidence.Approximate);

                var creation = diff.Compare(null, added);
                return new FileChange(path, FileChangeKind.Added, creation.Additions, 0,
                    Diff: string.IsNullOrEmpty(creation.Diff) ? null : creation.Diff,
                    Confidence: creation.IsExact ? FileChangeConfidence.Measured : FileChangeConfidence.Approximate);
            }

            if (before.Text is null || after.Text is null)
                // Unreadable or oversized on at least one side: report the change, not fake counts.
                return before.Length == after.Length && before.ModifiedAt == after.ModifiedAt
                    ? null
                    : new FileChange(path, FileChangeKind.Modified, null, null,
                        IsBinary: true, Confidence: FileChangeConfidence.Approximate);

            if (string.Equals(before.Text, after.Text, StringComparison.Ordinal)) return null;

            var difference = diff.Compare(before.Text, after.Text);
            return new FileChange(path, FileChangeKind.Modified, difference.Additions, difference.Deletions,
                Diff: string.IsNullOrEmpty(difference.Diff) ? null : difference.Diff,
                Confidence: difference.IsExact ? FileChangeConfidence.Measured : FileChangeConfidence.Approximate);
        }

        private static int Lines(string text) =>
            text.Length == 0 ? 0 : text.ReplaceLineEndings("\n").Split('\n').Length;

        private static Baseline Read(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return Baseline.Missing;
                var order = Baseline.Next();
                if (info.Length > MaxTrackedBytes) return new Baseline(true, null, info.Length, info.LastWriteTimeUtc, order);
                var text = File.ReadAllText(path);
                // A NUL byte is the usual cheap tell for binary content, where line counts are noise.
                return text.Contains('\0', StringComparison.Ordinal)
                    ? new Baseline(true, null, info.Length, info.LastWriteTimeUtc, order)
                    : new Baseline(true, text, info.Length, info.LastWriteTimeUtc, order);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException
                                              or NotSupportedException or ArgumentException)
            {
                // The tracker is an observer: a file it cannot read must not fail the run.
                return Baseline.Missing;
            }
        }

        /// <summary>
        /// Canonicalizes a reported path and refuses anything outside the run's grants. The path
        /// comes back from a tool result, so it is re-checked here rather than trusted.
        /// </summary>
        private string? Canonical(string path)
        {
            string full;
            try
            {
                if (!Path.IsPathFullyQualified(path)) return null;
                full = Path.GetFullPath(path);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }

            foreach (var root in grantRoots)
            {
                string canonicalRoot;
                try { canonicalRoot = Path.GetFullPath(root); }
                catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
                if (full.Equals(canonicalRoot, StringComparison.OrdinalIgnoreCase)) return full;
                var prefix = canonicalRoot.EndsWith(Path.DirectorySeparatorChar) ? canonicalRoot : canonicalRoot + Path.DirectorySeparatorChar;
                if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return full;
            }
            return null;
        }
    }

    /// <summary>
    /// A path as it stood when the run first reached for it.
    /// </summary>
    /// <param name="Order">
    /// When it was captured, relative to every other baseline this Host has taken. A path a turn and
    /// its subtask both touched is held twice, and the totals are only right if the older of the two
    /// is the one kept — comparing timestamps would not settle it, because two captures inside the
    /// same tick are exactly the case that arises when subtasks run at once.
    /// </param>
    private readonly record struct Baseline(bool Exists, string? Text, long Length, DateTime ModifiedAt, long Order)
    {
        private static long _taken;

        public static Baseline Missing => new(false, null, 0, default, Next());

        public static long Next() => Interlocked.Increment(ref _taken);
    }
}
