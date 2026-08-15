using System.Collections.Concurrent;
using System.Threading.Channels;
using AI.Client.Application.Chat;
using AI.Client.Application.Chats;
using AI.Client.Application.Projects;
using AI.Client.Application.Runs;
using AI.Client.Application.Settings;
using AI.Client.Contracts.Chat;
using AI.Client.Contracts.Chats;
using AI.Client.Contracts.Runs;
using AI.Client.Domain.Runs;

namespace AI.Client.Host;

internal sealed class ChatRunDispatcher(
    IChatRunRepository repository,
    IChatService chats,
    IProjectService projects,
    IGlobalSettingsService settings,
    IChatCompletionClient completionClient,
    IGlobalSecretStore secretStore,
    IClock clock) : IChatRunDispatcher
{
    private static readonly TimeSpan StreamingUpdateInterval = TimeSpan.FromMilliseconds(150);
    private readonly ConcurrentDictionary<RunKey, ChatRuntime> _runtimes = [];
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _chatWrites = [];
    private readonly ConcurrentDictionary<Guid, Channel<IReadOnlyList<ChatRunSnapshot>>> _subscribers = [];

    public async Task<ChatRunSnapshot> EnqueueAsync(Guid projectId, Guid chatId, EnqueueChatMessageRequest request, CancellationToken cancellationToken)
    {
        var runtime = await GetRuntimeAsync(projectId, chatId, request.BranchId, cancellationToken);
        lock (runtime.Sync)
        {
            runtime.State.Enqueue(request.OperationId, new QueuedRunMessage(request.MessageId, request.Content, clock.UtcNow, request.ParentMessageId));
        }
        await repository.SaveAsync(runtime.State, cancellationToken);
        Publish();
        StartWorker(runtime);
        return ToSnapshot(runtime.State);
    }

    public async Task<IReadOnlyList<ChatRunSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken) =>
        (await repository.ListAsync(cancellationToken)).Select(ToSnapshot).ToArray();

    public async IAsyncEnumerable<IReadOnlyList<ChatRunSnapshot>> SubscribeAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        // Capacity was 1 with DropOldest — fine for high-frequency streaming-chunk updates (each
        // one supersedes the last anyway), but a single queued run cycles through several
        // semantically distinct Publish() calls in quick succession with no real delay between
        // them (Complete() for one queue item immediately followed by Start() for the next).
        // With capacity 1, the "Completed" snapshot — the one Home.razor's OnRunSnapshot watches
        // for (Generating -> non-Generating) to know it's safe to refetch the chat and show the
        // newly materialized messages — routinely got overwritten by the next item's "Generating"
        // snapshot before this reader ever drained it. The symptom: messages/replies only ever
        // appeared once the ENTIRE queue finished, not as each item completed. A larger buffer
        // gives the reader room to catch up without losing any transition; DropOldest stays as a
        // safety net against a stalled/disconnected subscriber, not as the normal path.
        var channel = Channel.CreateBounded<IReadOnlyList<ChatRunSnapshot>>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });
        _subscribers[id] = channel;
        channel.Writer.TryWrite(await GetSnapshotAsync(cancellationToken));
        try { await foreach (var snapshot in channel.Reader.ReadAllAsync(cancellationToken)) yield return snapshot; }
        finally { _subscribers.TryRemove(id, out _); }
    }

    public async Task<ChatRunSnapshot?> StopAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken)
    {
        var runtime = await GetRuntimeAsync(projectId, chatId, branchId, cancellationToken);
        runtime.Cancellation?.Cancel();
        lock (runtime.Sync) { runtime.State.Pause(); }
        await repository.SaveAsync(runtime.State, cancellationToken); Publish(); return ToSnapshot(runtime.State);
    }

    public async Task<ChatRunSnapshot?> MarkReadAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken)
    {
        var runtime = await GetRuntimeAsync(projectId, chatId, branchId, cancellationToken);
        lock (runtime.Sync) { runtime.State.MarkRead(); }
        await repository.SaveAsync(runtime.State, cancellationToken); Publish(); return ToSnapshot(runtime.State);
    }

    public async Task<ChatRunSnapshot?> UpdateQueuedAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, UpdateQueuedMessageRequest request, CancellationToken cancellationToken)
    {
        var runtime = await GetRuntimeAsync(projectId, chatId, branchId, cancellationToken);
        lock (runtime.Sync) { if (request.Content is not null) runtime.State.Update(messageId, request.Content); if (request.Position is { } position) runtime.State.Move(messageId, position); }
        await repository.SaveAsync(runtime.State, cancellationToken); Publish(); return ToSnapshot(runtime.State);
    }

    public async Task<ChatRunSnapshot?> RemoveQueuedAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, Guid operationId, CancellationToken cancellationToken)
    {
        var runtime = await GetRuntimeAsync(projectId, chatId, branchId, cancellationToken);
        lock (runtime.Sync) runtime.State.Remove(messageId);
        await repository.SaveAsync(runtime.State, cancellationToken); Publish(); return ToSnapshot(runtime.State);
    }

    public async Task<ChatRunSnapshot?> ResumeAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken)
    {
        var runtime = await GetRuntimeAsync(projectId, chatId, branchId, cancellationToken);
        lock (runtime.Sync) runtime.State.Resume();
        await repository.SaveAsync(runtime.State, cancellationToken); Publish(); StartWorker(runtime); return ToSnapshot(runtime.State);
    }

    public async Task<ChatRunSnapshot?> ClearAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken cancellationToken)
    {
        var runtime = await GetRuntimeAsync(projectId, chatId, branchId, cancellationToken);
        lock (runtime.Sync) runtime.State.Clear();
        await repository.SaveAsync(runtime.State, cancellationToken); Publish(); return ToSnapshot(runtime.State);
    }

    public async Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await RemoveRuntimesAsync(key => key.ProjectId == projectId);
        await repository.DeleteProjectAsync(projectId, cancellationToken);
        Publish();
    }

    public async Task DeleteChatAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        await RemoveRuntimesAsync(key => key.ProjectId == projectId && key.ChatId == chatId);
        await repository.DeleteChatAsync(projectId, chatId, cancellationToken);
        Publish();
    }

    public async Task ReconcileChatAsync(Guid projectId, Guid chatId, IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken)
    {
        await RemoveRuntimesAsync(key => key.ProjectId == projectId && key.ChatId == chatId && !branchIds.Contains(key.BranchId));
        await repository.DeleteExceptAsync(projectId, chatId, branchIds, cancellationToken);
        Publish();
    }

    private async Task RemoveRuntimesAsync(Func<RunKey, bool> predicate)
    {
        var removed = _runtimes.Where(item => predicate(item.Key)).ToArray();
        foreach (var item in removed) item.Value.Cancellation?.Cancel();
        await Task.WhenAll(removed.Select(item => item.Value.Worker ?? Task.CompletedTask));
        foreach (var item in removed)
        {
            _runtimes.TryRemove(item.Key, out _);
            item.Value.Cancellation?.Dispose();
        }
    }

    public async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        foreach (var state in await repository.ListAsync(cancellationToken))
        {
            var key = new RunKey(state.ProjectId, state.ChatId, state.BranchId);
            // GetOrAdd, not indexer assignment: if something already raced this runtime into
            // existence (e.g. a request arrived while warm-up was still reading the repository),
            // that live instance is the one workers/pending operations reference — overwriting it
            // here would silently orphan whatever's already in flight against it.
            _runtimes.GetOrAdd(key, _ => new ChatRuntime(state));
        }
    }

    private async Task<ChatRuntime> GetRuntimeAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken)
    {
        var key = new RunKey(projectId, chatId, branchId);
        if (_runtimes.TryGetValue(key, out var existing)) return existing;
        var state = await repository.GetAsync(projectId, chatId, branchId, cancellationToken) ?? new ChatRunState(projectId, chatId, branchId);
        return _runtimes.GetOrAdd(key, _ => new ChatRuntime(state));
    }

    private void StartWorker(ChatRuntime runtime)
    {
        lock (runtime.Sync)
        {
            if (runtime.Worker is { IsCompleted: false } || runtime.State.Status is RunStatus.Paused or RunStatus.Interrupted or RunStatus.Failed) return;
            runtime.Worker = ProcessAsync(runtime);
        }
    }

    private async Task ProcessAsync(ChatRuntime runtime)
    {
        while (runtime.State.Queue.Count > 0)
        {
            var queued = runtime.State.Queue[0];
            runtime.State.Dequeue();
            runtime.Cancellation = new CancellationTokenSource();
            var token = runtime.Cancellation.Token;
            try
            {
                runtime.State.Start(); await repository.SaveAsync(runtime.State, token); Publish();
                var chat = await chats.GetAsync(runtime.State.ProjectId, runtime.State.ChatId, token) ?? throw new InvalidOperationException("Chat not found.");
                var project = await projects.GetAsync(runtime.State.ProjectId, token) ?? throw new InvalidOperationException("Project not found.");
                var global = await settings.GetAsync(token);
                var connectionId = chat.EndpointProfileId ?? project.ConnectionId
                    ?? throw new InvalidOperationException("No connection is configured for this chat or its project. Choose an endpoint before sending a message.");
                var connection = global.Connections.SingleOrDefault(item => item.Id == connectionId)
                    ?? throw new InvalidOperationException("The configured connection no longer exists. Choose another endpoint.");
                ChatDetails user;
                var chatWrite = _chatWrites.GetOrAdd(runtime.State.ChatId, _ => new SemaphoreSlim(1, 1));
                await chatWrite.WaitAsync(token);
                try
                {
                    chat = await chats.GetAsync(runtime.State.ProjectId, runtime.State.ChatId, token) ?? throw new InvalidOperationException("Chat not found.");
                    user = await chats.AppendMessageAsync(runtime.State.ProjectId, runtime.State.ChatId, new AppendChatMessageRequest(queued.Id, queued.ParentMessageId ?? (chat.Messages.Count > 0 ? chat.Messages[^1].Id : null), "User", queued.Content, chat.Revision), token) ?? throw new InvalidOperationException("Message conflict.");
                }
                finally { chatWrite.Release(); }
                var context = user.Messages.Select(message => new ChatCompletionMessage(message.Role.ToLowerInvariant(), message.Content)).ToArray();
                await foreach (var chunk in completionClient.StreamAsync(new ChatCompletionRequest(connection.BaseUrl, connection.Model, await secretStore.GetAsync("connection", connection.Id, token), queued.Content, null, context), token))
                {
                    runtime.State.Append(chunk.Content);
                    var now = clock.UtcNow;
                    if (now - runtime.LastStreamingUpdate >= StreamingUpdateInterval)
                    {
                        runtime.LastStreamingUpdate = now;
                        await repository.SaveAsync(runtime.State, token);
                        Publish();
                    }
                }
                await repository.SaveAsync(runtime.State, token);
                Publish();
                await chatWrite.WaitAsync(token);
                try
                {
                    var latest = await chats.GetAsync(runtime.State.ProjectId, runtime.State.ChatId, token) ?? throw new InvalidOperationException("Chat not found.");
                    _ = await chats.AppendMessageAsync(runtime.State.ProjectId, runtime.State.ChatId, new AppendChatMessageRequest(null, user.Messages[^1].Id, "Assistant", runtime.State.StreamingContent, latest.Revision), token) ?? throw new InvalidOperationException("Response conflict.");
                }
                finally { chatWrite.Release(); }
                runtime.State.Complete(true); await repository.SaveAsync(runtime.State, token);
                var completedChat = await chats.GetAsync(runtime.State.ProjectId, runtime.State.ChatId, token);
                if (completedChat is not null)
                {
                    await repository.DeleteExceptAsync(runtime.State.ProjectId, runtime.State.ChatId, ChatBranchIds.Get(completedChat), token);
                }
                Publish();
            }
            catch (OperationCanceledException) { runtime.State.Pause(); await repository.SaveAsync(runtime.State, CancellationToken.None); Publish(); return; }
            catch (Exception error) { runtime.State.Fail(error.Message); await repository.SaveAsync(runtime.State, CancellationToken.None); Publish(); return; }
            finally { runtime.Cancellation?.Dispose(); runtime.Cancellation = null; }
        }
    }

    private void Publish()
    {
        var snapshot = _runtimes.Values.Select(runtime => ToSnapshot(runtime.State)).ToArray();
        foreach (var subscriber in _subscribers.Values) subscriber.Writer.TryWrite(snapshot);
    }

    private static ChatRunSnapshot ToSnapshot(ChatRunState state) => new(state.ProjectId, state.ChatId, state.BranchId,
        Enum.Parse<ChatRunStatus>(state.Status.ToString()), state.StreamingContent,
        state.Queue.Select(item => new QueuedChatMessage(item.Id, item.Content, item.CreatedAt, item.ParentMessageId)).ToArray(), state.HasUnreadResponse, state.Error, state.Revision);

    private sealed class ChatRuntime(ChatRunState state)
    {
        public object Sync { get; } = new(); public ChatRunState State { get; } = state;
        public Task? Worker { get; set; } public CancellationTokenSource? Cancellation { get; set; }
        public DateTimeOffset LastStreamingUpdate { get; set; }
    }
    private readonly record struct RunKey(Guid ProjectId, Guid ChatId, Guid BranchId);
}
