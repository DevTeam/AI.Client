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
/// <remarks>
/// Only the Host's own file tools report effects precisely enough to be trusted here. Anything else
/// that could write — an external process, a third-party server — marks the run incomplete instead
/// of being guessed at, because claiming a complete list that is not complete is worse than
/// admitting the gap.
/// </remarks>
public sealed class WorkspaceChangeTracker : IWorkspaceChangeTracker
{
    /// <summary>Built-in tools whose arguments name a path they are about to modify.</summary>
    private static readonly Dictionary<string, string[]> MutatingPathArguments = new(StringComparer.Ordinal)
    {
        ["write_file"] = ["path"],
        ["edit_file"] = ["path"],
        ["create_directory"] = ["path"],
        ["move_file"] = ["source", "destination"],
    };

    /// <summary>
    /// Built-in tools that can change the workspace in ways this tracker cannot follow. A command
    /// can write anywhere it has permission to.
    /// </summary>
    private static readonly HashSet<string> OpaqueTools = new(StringComparer.Ordinal) { "process_run" };

    /// <summary>Past this size a file is compared by existence only; reading it twice is not worth it.</summary>
    private const long MaxTrackedBytes = 8 * 1024 * 1024;

    private readonly ConcurrentDictionary<WorkspaceRunKey, RunState> _runs = new();

    public Task BeginRunAsync(WorkspaceRunKey run, IReadOnlyList<ToolDirectoryGrant> grants, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);
        _runs[run] = new RunState(grants.Select(grant => grant.Root).ToArray());
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
        WorkspaceRunKey run, ToolDescriptor tool, string arguments, ToolCallResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(result);
        if (!_runs.TryGetValue(run, out var state)) return Task.CompletedTask;

        var reference = ToolRef.Parse(tool.Name);
        if (reference.IsBuiltIn && OpaqueTools.Contains(reference.Name))
        {
            state.MarkIncomplete();
            return Task.CompletedTask;
        }

        // A tool from any other server that is not declared read-only could have written something
        // the Host never saw. Annotations are hints, so this is a floor, not a verdict.
        if (!reference.IsBuiltIn && tool.Annotations is not { ReadOnlyHint: true })
        {
            state.MarkIncomplete();
            return Task.CompletedTask;
        }

        // A failed call may still have written something, so the paths stay tracked either way;
        // the comparison below simply finds no difference when nothing happened.
        foreach (var path in PathsFor(tool, arguments)) state.CaptureBaseline(path);
        return Task.CompletedTask;
    }

    public Task<WorkspaceChangeSet> SnapshotAsync(WorkspaceRunKey run, CancellationToken cancellationToken) =>
        Task.FromResult(_runs.TryGetValue(run, out var state) ? state.Snapshot() : WorkspaceChangeSet.Empty);

    public Task CompleteRunAsync(WorkspaceRunKey run, CancellationToken cancellationToken)
    {
        _runs.TryRemove(run, out _);
        return Task.CompletedTask;
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

    private sealed class RunState(IReadOnlyList<string> grantRoots)
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, Baseline> _baselines = new(StringComparer.OrdinalIgnoreCase);
        private bool _complete = true;

        public void MarkIncomplete()
        {
            lock (_gate) _complete = false;
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

        public WorkspaceChangeSet Snapshot()
        {
            List<KeyValuePair<string, Baseline>> tracked;
            bool complete;
            lock (_gate)
            {
                tracked = [.. _baselines];
                complete = _complete;
            }

            var files = new List<FileChange>();
            var additions = 0;
            var deletions = 0;
            foreach (var (path, baseline) in tracked)
            {
                var current = Read(path);
                if (Describe(path, baseline, current) is not { } change) continue;
                files.Add(change);
                additions += change.Additions ?? 0;
                deletions += change.Deletions ?? 0;
            }

            files.Sort((left, right) => string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase));
            return new WorkspaceChangeSet(files, additions, deletions, complete);
        }

        private static FileChange? Describe(string path, Baseline before, Baseline after)
        {
            if (!before.Exists && !after.Exists) return null;

            if (before.Exists && !after.Exists)
                return new FileChange(path, FileChangeKind.Deleted, 0, before.Text is { } text ? Lines(text) : null,
                    IsBinary: before.Text is null,
                    Confidence: before.Text is null ? FileChangeConfidence.Approximate : FileChangeConfidence.Measured);

            if (!before.Exists && after.Exists)
                return new FileChange(path, FileChangeKind.Added, after.Text is { } added ? Lines(added) : null, 0,
                    Diff: null,
                    IsBinary: after.Text is null,
                    Confidence: after.Text is null ? FileChangeConfidence.Approximate : FileChangeConfidence.Measured);

            if (before.Text is null || after.Text is null)
                // Unreadable or oversized on at least one side: report the change, not fake counts.
                return before.Length == after.Length && before.ModifiedAt == after.ModifiedAt
                    ? null
                    : new FileChange(path, FileChangeKind.Modified, null, null,
                        IsBinary: true, Confidence: FileChangeConfidence.Approximate);

            if (string.Equals(before.Text, after.Text, StringComparison.Ordinal)) return null;

            var difference = LineDiff.Compare(before.Text, after.Text);
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
                if (info.Length > MaxTrackedBytes) return new Baseline(true, null, info.Length, info.LastWriteTimeUtc);
                var text = File.ReadAllText(path);
                // A NUL byte is the usual cheap tell for binary content, where line counts are noise.
                return text.Contains('\0', StringComparison.Ordinal)
                    ? new Baseline(true, null, info.Length, info.LastWriteTimeUtc)
                    : new Baseline(true, text, info.Length, info.LastWriteTimeUtc);
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

        private readonly record struct Baseline(bool Exists, string? Text, long Length, DateTime ModifiedAt)
        {
            public static Baseline Missing => new(false, null, 0, default);
        }
    }
}
