namespace AI.Application.Chat;

using System.Collections.Concurrent;
using AI.Contracts.Chats;
using Projects;
using Tools;

/// <summary>
/// Run-local model projection overrides, and the history checkpoints a run makes. Stored chat
/// messages are never changed: a turn checkpoint lives as long as its run, and a history checkpoint
/// is kept with the chat, where the requests of later turns pick it up.
/// </summary>
public sealed class ModelContentCheckpointService(
    IContextSummaryWriter summaryWriter,
    IHistoryCheckpointService history,
    IClock clock,
    IIdGenerator ids) : IModelContentCheckpointService
{
    /// <summary>
    /// A history compaction always leaves the turn in progress, and the one before it when both fit
    /// the run's keep budget: that is usually what the current request is about.
    /// </summary>
    private const int HistoryTurnsToKeep = 2;

    private readonly ConcurrentDictionary<Key, Entry> _entries = new();

    public IDisposable Begin(ToolRunContext run, string model, long historyKeepTokens,
        Func<string, CancellationToken, Task<string>> summarize)
    {
        var key = Key.Of(run);
        _entries[key] = new Entry(model, new HistoryKeepPolicy(historyKeepTokens, 1, HistoryTurnsToKeep), summarize);
        return new Scope(() => _entries.TryRemove(key, out _));
    }

    public void Update(ToolRunContext run, IReadOnlyList<ChatCompletionMessage> context)
    {
        if (_entries.TryGetValue(Key.Of(run), out var entry)) entry.Context = context.ToArray();
    }

    public IReadOnlyList<ChatCompletionMessage> Apply(ToolRunContext run, IReadOnlyList<ChatCompletionMessage> context)
    {
        if (!_entries.TryGetValue(Key.Of(run), out var entry)) return context;
        if (entry.History is { } pinned) context = history.Apply(context, pinned);
        if (entry.Checkpoint is not { } checkpoint) return context;
        var boundary = FindBoundary(context, checkpoint.BoundaryCallId);
        var user = FindCurrentUser(context, boundary);
        if (boundary <= user + 1) return context;
        return context.Take(user + 1)
            .Append(new ChatCompletionMessage("user", "Compacted completed work from this turn:\n" + checkpoint.Summary))
            .Concat(context.Skip(boundary)).ToArray();
    }

    public void PinHistory(ToolRunContext run, HistoryCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (_entries.TryGetValue(Key.Of(run), out var entry)) entry.History = checkpoint;
    }

    public ModelContentCompactionPreview Preview(ToolRunContext run, ContextCompactionScope scope = ContextCompactionScope.Turn)
    {
        if (!_entries.TryGetValue(Key.Of(run), out var entry)) return new(0, 0, false);
        if (scope == ContextCompactionScope.History)
        {
            var coverable = history.Coverable(entry.Context, entry.Keep);
            return new(coverable.Count, coverable.Sum(message => (long)message.ForModel.Length), coverable.Count > 0);
        }
        var range = Range(entry.Context);
        return new(range.Count, range.Characters, range.Count > 0);
    }

    public Task<ModelContentCompactionResult> CompactAsync(ToolRunContext run, int targetTokens,
        ContextCompactionScope scope, CancellationToken cancellationToken)
    {
        if (!_entries.TryGetValue(Key.Of(run), out var entry))
            return Task.FromResult(new ModelContentCompactionResult(0, 0, 0, false, "No active run context is available."));
        return scope == ContextCompactionScope.History
            ? CompactHistoryAsync(run, entry, targetTokens, cancellationToken)
            : CompactTurnAsync(entry, targetTokens, cancellationToken);
    }

    public async Task<bool> ResetAsync(ToolRunContext run, ContextCompactionScope scope, CancellationToken cancellationToken)
    {
        if (!_entries.TryGetValue(Key.Of(run), out var entry)) return false;
        if (scope == ContextCompactionScope.Turn) return Interlocked.Exchange(ref entry.Checkpoint, null) is not null;
        // The history the run sees is either the checkpoint it pinned or the one its context was
        // built with, which leads that context as its summary message.
        var active = entry.History ?? history.Find(
            await history.ListAsync(run.ProjectId, run.ChatId, cancellationToken), entry.Context);
        if (active is null) return false;
        entry.History = null;
        await history.DeleteAsync(run.ProjectId, run.ChatId, active.Id, cancellationToken);
        return true;
    }

    private async Task<ModelContentCompactionResult> CompactTurnAsync(Entry entry, int targetTokens,
        CancellationToken cancellationToken)
    {
        var range = Range(entry.Context);
        if (range.Count == 0) return new(0, 0, 0, false, "There is no completed work in this turn to compact.");
        var summary = await summaryWriter.WriteAsync(entry.Context.Skip(range.Start).Take(range.Count).ToArray(),
            targetTokens, new Summarizer(entry.Summarize), cancellationToken);
        if (summary is null) return new(range.Count, range.Characters, 0, false, "The compaction task returned no summary.");
        entry.Checkpoint = new Checkpoint(range.BoundaryCallId, summary.Text);
        return new(range.Count, range.Characters, summary.Text.Length, true,
            "Completed work was replaced only in the model-facing projection. Stored chat content is unchanged.");
    }

    private async Task<ModelContentCompactionResult> CompactHistoryAsync(ToolRunContext run, Entry entry, int targetTokens,
        CancellationToken cancellationToken)
    {
        // The context the run was given may already open with a summary; covering it again folds
        // the older summary into the new one.
        var context = entry.History is { } pinned ? history.Apply(entry.Context, pinned) : entry.Context;
        var coverable = history.Coverable(context, entry.Keep);
        if (coverable.Count == 0)
            return new(0, 0, 0, false, "Only the current turn and the recent ones kept in full are left; there is no earlier history to compact.");
        var summary = await summaryWriter.WriteAsync(coverable, targetTokens, new Summarizer(entry.Summarize), cancellationToken);
        var sourceCharacters = coverable.Sum(message => (long)message.ForModel.Length);
        if (summary is null) return new(coverable.Count, sourceCharacters, 0, false, "The compaction task returned no summary.");
        var checkpoint = new HistoryCheckpoint(ids.Create(), coverable.Last(message => message.MessageId is not null).MessageId!.Value,
            summary.Text, coverable.Count, summary.SourceCharacters, entry.Model, clock.UtcNow, HistoryCheckpointOrigin.Model);
        await history.AddAsync(run.ProjectId, run.ChatId, checkpoint, cancellationToken);
        entry.History = checkpoint;
        return new(coverable.Count, sourceCharacters, summary.Text.Length, true,
            "Earlier turns were replaced by a summary in this and every later request of this branch. "
            + "Stored chat content and the visible transcript are unchanged; Reset with scope History restores the full history.");
    }

    private static RangeInfo Range(IReadOnlyList<ChatCompletionMessage> context)
    {
        var boundary = context.Select((message, index) => (message, index)).LastOrDefault(item =>
            item.message.ToolCalls?.Any(call => call.Name.EndsWith("_context_compact", StringComparison.Ordinal)) == true);
        if (boundary.message is null) return new(0, 0, 0, string.Empty);
        var user = FindCurrentUser(context, boundary.index);
        var count = Math.Max(0, boundary.index - user - 1);
        var characters = context.Skip(user + 1).Take(count).Sum(item => (long)item.ForModel.Length);
        var callId = boundary.message.ToolCalls!.First(call => call.Name.EndsWith("_context_compact", StringComparison.Ordinal)).Id;
        return new(user + 1, count, characters, callId);
    }

    private static int FindCurrentUser(IReadOnlyList<ChatCompletionMessage> context, int before) =>
        context.Take(before).Select((message, index) => (message, index)).LastOrDefault(item => item.message.Role == "user").index;

    private static int FindBoundary(IReadOnlyList<ChatCompletionMessage> context, string callId) =>
        context.Select((message, index) => (message, index))
            .FirstOrDefault(item => item.message.ToolCalls?.Any(call => call.Id == callId) == true).index;

    private readonly record struct Key(Guid ProjectId, Guid ChatId, Guid BranchId)
    {
        public static Key Of(ToolRunContext run) => new(run.ProjectId, run.ChatId, run.BranchId);
    }

    private sealed class Entry(string model, HistoryKeepPolicy keep, Func<string, CancellationToken, Task<string>> summarize)
    {
        public string Model { get; } = model;
        public HistoryKeepPolicy Keep { get; } = keep;
        public Func<string, CancellationToken, Task<string>> Summarize { get; } = summarize;
        public IReadOnlyList<ChatCompletionMessage> Context { get; set; } = [];
        public Checkpoint? Checkpoint;
        public HistoryCheckpoint? History { get; set; }
    }

    private sealed class Summarizer(Func<string, CancellationToken, Task<string>> summarize) : IContextSummarizer
    {
        public Task<string> SummarizeAsync(string prompt, CancellationToken cancellationToken) => summarize(prompt, cancellationToken);
    }

    private sealed record Checkpoint(string BoundaryCallId, string Summary);
    private readonly record struct RangeInfo(int Start, int Count, long Characters, string BoundaryCallId);

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
