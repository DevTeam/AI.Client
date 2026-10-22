namespace AI.Application.Runs;

using System.Text.Json;
using AI.Application.Skills;
using AI.Contracts.Runs;
using AI.Application.Users;
using AI.Domain.Users;

/// <summary>
/// Drafts of the user's next message, one per branch, kept in memory for the answer they were
/// written for. A draft is started when an answer lands, so it is usually ready by the time the
/// user looks; asking for it while it is still being written waits for it.
/// </summary>
public interface IChatReplySuggestions
{
    /// <summary>Starts drafting a reply to <paramref name="leafMessageId"/> unless one is already there.</summary>
    void Start(Guid projectId, Guid chatId, Guid branchId, Guid leafMessageId);

    /// <summary>
    /// The draft for <paramref name="leafMessageId"/>, waiting for it if it is being written. With
    /// <paramref name="generate"/> a missing or empty draft is written now; otherwise there is none.
    /// </summary>
    Task<ChatReplySuggestion?> GetAsync(Guid projectId, Guid chatId, Guid branchId, Guid leafMessageId, bool generate,
        CancellationToken cancellationToken);
}

public sealed class ChatReplySuggestions(ISkillRunner skills, IUserContext users, IUserScope userScope)
    : IChatReplySuggestions, IAsyncDisposable
{
    // Only the branches someone recently got an answer in matter; older drafts are dropped first.
    private const int Capacity = 64;
    private readonly Lock _gate = new();
    private readonly Dictionary<(Guid ChatId, Guid BranchId), Entry> _entries = [];
    private readonly CancellationTokenSource _shutdown = new();
    private long _sequence;

    // The user is part of what a draft is for: another user naming the same ids gets none of it.
    private sealed record Entry(UserId User, Guid ProjectId, Guid LeafMessageId, Task<string?> Text, long Sequence);

    public void Start(Guid projectId, Guid chatId, Guid branchId, Guid leafMessageId) =>
        Ensure(projectId, chatId, branchId, leafMessageId, retry: false);

    public async Task<ChatReplySuggestion?> GetAsync(Guid projectId, Guid chatId, Guid branchId, Guid leafMessageId,
        bool generate, CancellationToken cancellationToken)
    {
        Task<string?>? task;
        lock (_gate)
            task = _entries.TryGetValue((chatId, branchId), out var entry)
                && entry.User == users.Current && entry.ProjectId == projectId && entry.LeafMessageId == leafMessageId
                ? entry.Text : null;
        if (generate) task = Ensure(projectId, chatId, branchId, leafMessageId, retry: true);
        if (task is null) return null;
        var text = await task.WaitAsync(cancellationToken);
        return text is null ? null : new ChatReplySuggestion(leafMessageId, text);
    }

    private Task<string?>? Ensure(Guid projectId, Guid chatId, Guid branchId, Guid leafMessageId, bool retry)
    {
        lock (_gate)
        {
            if (_shutdown.IsCancellationRequested) return null;
            var key = (chatId, branchId);
            var user = users.Current;
            // A draft that came back empty or failed is written again only when someone asks for it.
            if (_entries.TryGetValue(key, out var existing) && existing.User == user && existing.ProjectId == projectId
                && existing.LeafMessageId == leafMessageId
                && !(retry && existing.Text is { IsCompleted: true, Result: null }))
                return existing.Text;
            var task = userScope.Run(user, () => DraftAsync(projectId, chatId, branchId, leafMessageId, _shutdown.Token));
            _entries[key] = new Entry(user, projectId, leafMessageId, task, ++_sequence);
            if (_entries.Count > Capacity) _entries.Remove(_entries.MinBy(item => item.Value.Sequence).Key);
            return task;
        }
    }

    private async Task<string?> DraftAsync(Guid projectId, Guid chatId, Guid branchId, Guid leafMessageId,
        CancellationToken cancellationToken)
    {
        var record = await skills.RunAsync(new SkillInvocation("chat-reply-suggest", projectId,
            JsonSerializer.SerializeToElement(new { chat_id = chatId, branch_id = branchId, message_id = leafMessageId }),
            chatId, branchId), cancellationToken);
        return record is { Status: "Completed", Output: { ValueKind: JsonValueKind.Object } output }
            && output.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString()
            : null;
    }

    public async ValueTask DisposeAsync()
    {
        Task[] tasks;
        lock (_gate)
        {
            _shutdown.Cancel();
            tasks = _entries.Values.Select(entry => (Task)entry.Text).ToArray();
        }
        // The runner turns cancellation and failure into a result, so waiting cannot throw.
        await Task.WhenAll(tasks);
        _shutdown.Dispose();
    }
}
