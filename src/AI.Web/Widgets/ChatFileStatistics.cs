namespace AI.Web.Widgets;

using AI.Contracts.Chats;
using AI.Contracts.Workspace;
using AI.Web.Components;

/// <summary>One file across the turns in scope.</summary>
/// <param name="Kind">What the file is now compared with before its first change in scope.</param>
/// <param name="Additions">
/// Lines added, summed over the runs that touched the file: a line written in one turn and removed
/// in the next counts on both sides.
/// </param>
/// <param name="LatestSourceMessageId">
/// The newest saved receipt that holds this file, which is where its review opens; null while the
/// only change is in a run still going.
/// </param>
/// <param name="IsApproximate">Some run could not measure this file's lines, so its counts are short.</param>
public sealed record ChatFileStatistic(
    string Path,
    FileChangeKind Kind,
    int Additions,
    int Deletions,
    bool IsBinary,
    bool IsApproximate,
    Guid? LatestSourceMessageId,
    string? PreviousPath = null);

public sealed record ChatFileStatistics(
    IReadOnlyList<ChatFileStatistic> Files,
    int Additions,
    int Deletions,
    int Turns,
    int TurnsWithChanges,
    int Sources,
    Guid? LatestSourceMessageId,
    bool IncludesLive)
{
    public static ChatFileStatistics Empty { get; } = new([], 0, 0, 0, 0, 0, null, false);

    public int Count(FileChangeKind kind) => Files.Count(file => file.Kind == kind);
}

/// <summary>Opens the review of one saved receipt, at <paramref name="Path"/> when one is given.</summary>
public sealed record ChatFileReviewRequest(Guid SourceMessageId, string? Path);

/// <summary>
/// Adds up the file changes of a chat branch. Each saved receipt is the net change of one run; the
/// statistics sum them, so a file edited in three turns appears once with all its edits counted.
/// </summary>
public interface IChatFileStatisticsCalculator
{
    /// <param name="branch">The visible branch, root to leaf.</param>
    /// <param name="live">The changes of the run in progress, before they are saved as a receipt.</param>
    ChatFileStatistics Calculate(IReadOnlyList<ChatMessageView> branch, WorkspaceChangeSet? live, ChatWidgetScope scope);
}

public sealed class ChatFileStatisticsCalculator(IChatFeedProjection feed) : IChatFileStatisticsCalculator
{
    public ChatFileStatistics Calculate(IReadOnlyList<ChatMessageView> branch, WorkspaceChangeSet? live, ChatWidgetScope scope)
    {
        var turns = SplitTurns(branch);
        if (scope == ChatWidgetScope.LastTurn && turns.Count > 1) turns = turns[^1..];

        // The receipt and the run snapshot arrive independently; once the receipt is in, the live
        // copy describes the same edits and would count them twice.
        var includeLive = live is { IsEmpty: false } && !feed.LastTurnHasWorkspaceReceipt(branch);
        var files = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
        var turnsWithChanges = 0;
        var sources = 0;
        Guid? latestSource = null;
        for (var index = 0; index < turns.Count; index++)
        {
            var changed = false;
            foreach (var message in turns[index])
            {
                if (message.WorkspaceChanges is not { IsEmpty: false } changes) continue;
                Add(files, changes, message.Id);
                changed = true;
                sources++;
                latestSource = message.Id;
            }
            if (includeLive && index == turns.Count - 1)
            {
                Add(files, live!, null);
                changed = true;
            }
            if (changed) turnsWithChanges++;
        }
        if (includeLive && turns.Count == 0)
        {
            Add(files, live!, null);
            turnsWithChanges++;
        }

        var result = files.Values.Select(item => item.ToStatistic())
            .OrderBy(file => file.IsBinary)
            .ThenByDescending(file => file.Additions + file.Deletions)
            .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new ChatFileStatistics(result, result.Sum(file => file.Additions), result.Sum(file => file.Deletions),
            Math.Max(turns.Count, includeLive ? 1 : 0), turnsWithChanges, sources, latestSource, includeLive);
    }

    // A turn starts at a person's message; whatever comes before the first one belongs to it.
    private static List<List<ChatMessageView>> SplitTurns(IReadOnlyList<ChatMessageView> branch)
    {
        var turns = new List<List<ChatMessageView>>();
        foreach (var message in branch)
        {
            if (message.Role == "User" || turns.Count == 0) turns.Add([]);
            turns[^1].Add(message);
        }
        return turns;
    }

    private static void Add(Dictionary<string, Accumulator> files, WorkspaceChangeSet changes, Guid? sourceId)
    {
        foreach (var change in changes.Files)
        {
            // A rename carries the file's history over to its new path.
            if (change is { Kind: FileChangeKind.Renamed, PreviousPath: { } previous }
                && previous != change.Path && files.Remove(previous, out var moved))
            {
                if (files.TryGetValue(change.Path, out var existing)) existing.Merge(moved);
                else files[change.Path] = moved.MoveTo(change.Path);
            }
            if (!files.TryGetValue(change.Path, out var file)) files[change.Path] = file = new Accumulator(change.Path, change.Kind, change.PreviousPath);
            file.Add(change, sourceId);
        }
    }

    private sealed class Accumulator(string path, FileChangeKind firstKind, string? previousPath)
    {
        private readonly FileChangeKind _firstKind = firstKind;
        private string _path = path;
        private string? _origin = previousPath;
        private FileChangeKind _lastKind = firstKind;
        private bool _renamed;
        private int _additions, _deletions;
        private bool _binary, _approximate;
        private Guid? _source;

        public Accumulator MoveTo(string newPath)
        {
            _origin ??= _path;
            _path = newPath;
            return this;
        }

        public void Add(FileChange change, Guid? sourceId)
        {
            _lastKind = change.Kind;
            if (change.Kind == FileChangeKind.Renamed)
            {
                _renamed = true;
                _origin ??= change.PreviousPath;
            }
            _additions += change.Additions ?? 0;
            _deletions += change.Deletions ?? 0;
            _binary |= change.IsBinary;
            _approximate |= change.Confidence == FileChangeConfidence.Approximate
                            || !change.IsBinary && (change.Additions is null || change.Deletions is null);
            if (sourceId is not null) _source = sourceId;
        }

        public void Merge(Accumulator other)
        {
            _additions += other._additions;
            _deletions += other._deletions;
            _binary |= other._binary;
            _approximate |= other._approximate;
            _renamed |= other._renamed;
            _source ??= other._source;
        }

        public ChatFileStatistic ToStatistic()
        {
            var kind = _lastKind == FileChangeKind.Deleted ? FileChangeKind.Deleted
                : _firstKind == FileChangeKind.Added ? FileChangeKind.Added
                : _renamed ? FileChangeKind.Renamed
                : FileChangeKind.Modified;
            return new ChatFileStatistic(_path, kind, _additions, _deletions, _binary, _approximate, _source,
                kind == FileChangeKind.Renamed ? _origin : null);
        }
    }
}
