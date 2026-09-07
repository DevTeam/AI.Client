// ReSharper disable UseCollectionExpression
namespace AI.Client.Application.Runs;

using Chat;
using Tools;
using Chats;
using Projects;
using Settings;
using Contracts.Chat;
using Contracts.Chats;
using Contracts.Runs;
using Contracts.Projects;
using Domain.Runs;
using System.Collections.Concurrent;
using System.Threading.Channels;

public sealed class ChatRunDispatcher(
    IChatRunRepository repository, ChatService chats, IProjectService projects,
    IGlobalSettingsRepository settings, ChatAgent agent,
    IGlobalSecretStore secretStore, IClock clock, ChatSynchronization synchronization) : IChatRunDispatcher, IAsyncDisposable
{
    private readonly ConcurrentDictionary<RunKey, Runtime> _runtimes = new();
    private readonly ConcurrentDictionary<Guid, Channel<IReadOnlyList<ChatRunSnapshot>>> _subscribers = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Lock _publicationLock = new();
    private readonly ConcurrentDictionary<Guid, byte> _maintenance = new();
    private readonly ConcurrentDictionary<Guid, byte> _deletingProjects = new();

    public async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        foreach (var state in await repository.ListAsync(cancellationToken))
        {
            using var lease = await synchronization.EnterAsync(state.ChatId, cancellationToken);
            var chat = await chats.GetAsync(state.ProjectId, state.ChatId, cancellationToken);
            if (chat is null) continue;
            state.RecoverAfterRestart();
            await repository.SaveAsync(state, cancellationToken);
            var runtime = new Runtime(state)
            {
                Snapshot = Snapshot(state, chat)
            };
            _runtimes.TryAdd(new RunKey(state.ProjectId, state.ChatId, state.BranchId), runtime);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync(CancellationToken.None);
        _shutdown.Dispose();
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        await _shutdown.CancelAsync();
        await Task.WhenAll(_runtimes.Values.Select(runtime => runtime.Worker ?? Task.CompletedTask)).WaitAsync(cancellationToken);
        foreach (var subscriber in _subscribers.Values) subscriber.Writer.TryComplete();
    }

    public Task<IReadOnlyList<ChatRunSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ChatRunSnapshot>>(_runtimes.Values.Select(runtime => runtime.Snapshot).ToArray());
    }

    public async IAsyncEnumerable<IReadOnlyList<ChatRunSnapshot>> SubscribeAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<IReadOnlyList<ChatRunSnapshot>>(new BoundedChannelOptions(1)
            { FullMode = BoundedChannelFullMode.DropOldest });
        _subscribers[id] = channel;
        try
        {
            lock (_publicationLock) channel.Writer.TryWrite(_runtimes.Values.Select(runtime => runtime.Snapshot).ToArray());
            await foreach (var snapshot in channel.Reader.ReadAllAsync(cancellationToken)) yield return snapshot;
        }
        finally { _subscribers.TryRemove(id, out _); }
    }

    public async Task<ChatRunSnapshot> SubmitAsync(Guid projectId, Guid chatId, SubmitChatMessageRequest request, CancellationToken cancellationToken)
    {
        _shutdown.Token.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Content);
        if (request.OperationId == Guid.Empty || request.MessageId == Guid.Empty || !Enum.IsDefined(request.Mode))
            throw new ArgumentException("Valid operation, message and submission mode are required.");
        using var lease = await synchronization.EnterAsync(chatId, cancellationToken);
        if (_maintenance.ContainsKey(chatId) || _deletingProjects.ContainsKey(projectId)) throw new InvalidOperationException("Chat is being changed.");
        _ = await projects.GetAsync(projectId, cancellationToken) ?? throw new InvalidOperationException("Project not found.");
        var chat = await chats.GetAsync(projectId, chatId, cancellationToken) ?? throw new InvalidOperationException("Chat not found.");
        var branchId = request.Mode == ChatSubmitMode.Fork ? request.MessageId : request.BranchId ?? chat.Id;
        var runtime = await GetRuntimeAsync(projectId, chatId, branchId, chat, cancellationToken);
        if (runtime.State.Operations.Contains(request.OperationId)) return runtime.Snapshot;
        if (request.ExpectedRevision is { } revision && revision != chat.Revision)
            throw new InvalidOperationException("The chat changed. Reload it before submitting.");
        if (request.ParentMessageId is { } parent && chat.Messages.All(message => message.Id != parent))
            throw new ArgumentException("Parent message does not exist.");
        if (request.Mode != ChatSubmitMode.Fork && chat.Branches?.All(branch => branch.Id != branchId) == true
            && runtime.State.Queue.Count == 0 && branchId != request.MessageId)
            throw new ArgumentException("Branch does not exist.");
        Guid? replaceId = null;
        var parentId = request.ParentMessageId;
        if (request.Mode == ChatSubmitMode.Replace)
        {
            var source = chat.Messages.SingleOrDefault(message => message.Id == request.ReplaceSourceId)
                ?? throw new ArgumentException("Replacement message does not exist.");
            if (_runtimes.Values.Any(item => item.State.ChatId == chatId && item.State.Status == RunStatus.Generating))
                throw new InvalidOperationException("Stop the chat before replacing a message.");
            replaceId = source.Id;
            parentId = source.ParentId;
        }
        var before = Clone(runtime.State);
        runtime.State.Enqueue(request.OperationId, new QueuedRunMessage(request.MessageId, request.Content.Trim(), clock.UtcNow, parentId, replaceId));
        if (request.Mode == ChatSubmitMode.Queue || request.HoldInQueue)
        {
            runtime.ResumeRequested = false;
            if (runtime.Cancellation is { } cancellation)
            {
                await cancellation.CancelAsync();
            }

            runtime.State.Pause();
        }
        else
        {
            runtime.ResumeRequested = runtime.Cancellation?.IsCancellationRequested == true;
            runtime.State.Resume();
        }
        try { await SaveAsync(runtime, chat, cancellationToken); }
        catch { runtime.State = before; throw; }
        StartWorker(runtime);
        return runtime.Snapshot;
    }

    public Task<ChatRunSnapshot?> StopAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken, Guid? operationId = null) =>
        MutateAsync(projectId, chatId, branchId, runtime => { runtime.ResumeRequested = false; runtime.Cancellation?.Cancel(); runtime.State.Pause(); }, operationId, cancellationToken);
    public Task<ChatRunSnapshot?> MarkReadAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken, Guid? operationId = null) =>
        MutateAsync(projectId, chatId, branchId, runtime => runtime.State.MarkRead(), operationId, cancellationToken);
    public Task<ChatRunSnapshot?> ResumeAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken, Guid? operationId = null) =>
        MutateAsync(projectId, chatId, branchId, runtime => { runtime.ResumeRequested = true; runtime.State.Resume(); }, operationId, cancellationToken);
    public Task<ChatRunSnapshot?> ClearAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken, Guid? operationId = null) =>
        MutateAsync(projectId, chatId, branchId, runtime =>
        {
            foreach (var item in runtime.State.Queue.Where(item => item.Id != runtime.ActiveMessageId).ToArray()) runtime.State.Remove(item.Id);
        }, operationId, cancellationToken);
    public Task<ChatRunSnapshot?> UpdateQueuedAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, UpdateQueuedMessageRequest request, CancellationToken cancellationToken) =>
        MutateAsync(projectId, chatId, branchId, runtime =>
        {
            if (runtime.ActiveMessageId == messageId) throw new InvalidOperationException("Message is already running.");
            if (request.Content is not null) runtime.State.Update(messageId, request.Content);
            if (request.Position is { } position) runtime.State.Move(messageId, Math.Max(runtime.ActiveMessageId is null ? 0 : 1, position));
        }, request.OperationId, cancellationToken);
    public Task<ChatRunSnapshot?> RemoveQueuedAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, CancellationToken cancellationToken, Guid? operationId = null) =>
        MutateAsync(projectId, chatId, branchId, runtime =>
        {
            if (runtime.ActiveMessageId == messageId) throw new InvalidOperationException("Message is already running.");
            runtime.State.Remove(messageId);
        }, operationId, cancellationToken);

    private async Task<ChatRunSnapshot?> MutateAsync(Guid projectId, Guid chatId, Guid branchId, Action<Runtime> mutate, Guid? operationId, CancellationToken token)
    {
        using var lease = await synchronization.EnterAsync(chatId, token);
        if (_maintenance.ContainsKey(chatId) || _deletingProjects.ContainsKey(projectId)) throw new InvalidOperationException("Chat is being changed.");
        var chat = await chats.GetAsync(projectId, chatId, token);
        if (chat is null) return null;
        var runtime = await GetRuntimeAsync(projectId, chatId, branchId, chat, token);
        if (operationId is { } duplicate && runtime.State.Operations.Contains(duplicate)) return runtime.Snapshot;
        var before = Clone(runtime.State);
        try { if (operationId is { } id) runtime.State.RememberOperation(id); mutate(runtime); await SaveAsync(runtime, chat, token); }
        catch { runtime.State = before; throw; }
        StartWorker(runtime);
        return runtime.Snapshot;
    }

    private async Task<Runtime> GetRuntimeAsync(Guid projectId, Guid chatId, Guid branchId, ChatDetails chat, CancellationToken token)
    {
        var key = new RunKey(projectId, chatId, branchId);
        if (_runtimes.TryGetValue(key, out var runtime)) return runtime;
        var state = await repository.GetAsync(projectId, chatId, branchId, token) ?? new ChatRunState(projectId, chatId, branchId);
        runtime = new Runtime(state) { Snapshot = Snapshot(state, chat) };
        return _runtimes.GetOrAdd(key, runtime);
    }

    private void StartWorker(Runtime runtime)
    {
        if (_shutdown.IsCancellationRequested || runtime.Worker is { IsCompleted: false }
            || runtime.State.Queue.Count == 0 || runtime.State.Status is RunStatus.Paused or RunStatus.Interrupted or RunStatus.Failed) return;
        runtime.Worker = Task.Run(() => ProcessAsync(runtime));
    }

    private async Task ProcessAsync(Runtime runtime)
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                QueuedRunMessage queued;
                ChatCompletionRequest request;
                CancellationToken token;
                using (await synchronization.EnterAsync(runtime.State.ChatId, CancellationToken.None))
                {
                    if (runtime.State.Queue.Count == 0 || runtime.State.Status is RunStatus.Paused or RunStatus.Interrupted or RunStatus.Failed) return;
                    queued = runtime.State.Queue[0];
                    runtime.ActiveMessageId = queued.Id;
                    runtime.ResumeRequested = false;
                    runtime.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                    token = runtime.Cancellation.Token;
                    runtime.State.Start();
                    var chat = await chats.GetAsync(runtime.State.ProjectId, runtime.State.ChatId, token) ?? throw new InvalidOperationException("Chat not found.");
                    if (chat.Messages.Any(message => message.Id == ReplyId(queued.Id)))
                    {
                        runtime.State.Remove(queued.Id);
                        runtime.State.Complete(true);
                        runtime.ActiveMessageId = null;
                        runtime.Cancellation.Dispose();
                        runtime.Cancellation = null;
                        await SaveAsync(runtime, chat, token);
                        continue;
                    }
                    await SaveAsync(runtime, chat, token);
                    var project = await projects.GetAsync(chat.ProjectId, token) ?? throw new InvalidOperationException("Project not found.");
                    var global = await settings.LoadAsync(token);
                    var connectionId = chat.ConnectionId ?? project.ConnectionId;
                    var connection = global.Connections.SingleOrDefault(item => item.Id == connectionId && item.Enabled)
                        ?? throw new InvalidOperationException("Choose an enabled connection for this chat.");
                    // Retaining the command until completion makes restart/resume safe: an already committed user message is reused.
                    if (chat.Messages.All(message => message.Id != queued.Id))
                    {
                        var parent = queued.ParentMessageId ?? chat.Branches?.SingleOrDefault(branch => branch.Id == runtime.State.BranchId)?.HeadMessageId;
                        chat = await chats.AppendMessageCoreAsync(chat.ProjectId, chat.Id,
                            new AppendChatMessageRequest(queued.Id, parent, "User", queued.Content, chat.Revision,
                                BranchId: runtime.State.BranchId, ReplaceSourceId: queued.ReplaceSourceId), token)
                            ?? throw new InvalidOperationException("Message conflict.");
                    }
                    request = new ChatCompletionRequest(connection.BaseUrl, connection.Model,
                        await secretStore.GetAsync("connection", connection.Id, token), queued.Content, null, ChatContext.Get(chat, ResumeHead(chat, runtime.State.BranchId, queued.Id)));
                    runtime.ToolHead = ResumeHead(chat, runtime.State.BranchId, queued.Id);
                    await SaveAsync(runtime, chat, token);
                }

                await agent.RunAsync(runtime.State.ProjectId, request,
                    (message, ct) => PersistToolMessageAsync(runtime, message, ct),
                    async (content, ct) =>
                    {
                        using var lease = await synchronization.EnterAsync(runtime.State.ChatId, ct);
                        runtime.State.Append(content);
                        if (clock.UtcNow - runtime.LastPublished >= TimeSpan.FromMilliseconds(150))
                        {
                            runtime.LastPublished = clock.UtcNow;
                            await SaveAsync(runtime, await chats.GetAsync(runtime.State.ProjectId, runtime.State.ChatId, ct), ct);
                        }
                    },
                    async (name, ct) =>
                    {
                        using var lease = await synchronization.EnterAsync(runtime.State.ChatId, ct);
                        runtime.ActiveTool = name;
                        runtime.State.Append("");
                        await SaveAsync(runtime, await chats.GetAsync(runtime.State.ProjectId, runtime.State.ChatId, ct), ct);
                    },
                    (tool, arguments, timeout, ct) => ApproveAsync(runtime, tool, arguments, timeout, ct), token);
                using (await synchronization.EnterAsync(runtime.State.ChatId, token))
                {
                    token.ThrowIfCancellationRequested();
                    var chat = await chats.GetAsync(runtime.State.ProjectId, runtime.State.ChatId, token) ?? throw new InvalidOperationException("Chat not found.");
                    // Stable reply id avoids duplicate assistant messages if the process stops between chat and run commits.
                    var replyId = ReplyId(queued.Id);
                    if (chat.Messages.All(message => message.Id != replyId))
                        chat = await chats.AppendMessageCoreAsync(chat.ProjectId, chat.Id,
                            new AppendChatMessageRequest(replyId, runtime.ToolHead ?? queued.Id, "Assistant", runtime.State.StreamingContent, chat.Revision,
                                BranchId: runtime.State.BranchId), token) ?? throw new InvalidOperationException("Response conflict.");
                    runtime.State.Remove(queued.Id);
                    runtime.State.Complete(true);
                    runtime.ActiveMessageId = null;
                    runtime.Cancellation.Dispose();
                    runtime.Cancellation = null;
                    await SaveAsync(runtime, chat, token);
                }
            }
        }
        catch (Exception error)
        {
            using var lease = await synchronization.EnterAsync(runtime.State.ChatId, CancellationToken.None);
            if (error is OperationCanceledException) runtime.State.Pause(); else runtime.State.Fail(error.Message);
            if (error is OperationCanceledException && runtime.ResumeRequested && !_shutdown.IsCancellationRequested) runtime.State.Resume();
            try { await SaveAsync(runtime, await chats.GetAsync(runtime.State.ProjectId, runtime.State.ChatId, CancellationToken.None), CancellationToken.None); }
            catch (Exception saveError) when (saveError is IOException or UnauthorizedAccessException)
            {
                runtime.State.Fail(saveError.Message);
                runtime.Snapshot = Snapshot(runtime.State, null);
                Publish();
            }
        }
        finally
        {
            using var lease = await synchronization.EnterAsync(runtime.State.ChatId, CancellationToken.None);
            runtime.Cancellation?.Dispose();
            runtime.Cancellation = null;
            runtime.ActiveMessageId = null;
            runtime.Approval = null;
            runtime.PendingApproval = null;
            runtime.ActiveTool = null;
            runtime.Snapshot = runtime.Snapshot with { PendingApproval = null, ActiveTool = null };
            Publish();
            runtime.Worker = null;
            StartWorker(runtime);
        }
    }

    public async Task<bool> DecideToolAsync(Guid projectId, Guid chatId, Guid branchId, ToolApprovalDecision decision, CancellationToken token)
    {
        using var lease = await synchronization.EnterAsync(chatId, token);
        if (!_runtimes.TryGetValue(new RunKey(projectId, chatId, branchId), out var runtime)
            || runtime.PendingApproval?.Id != decision.ApprovalId || runtime.Cancellation?.IsCancellationRequested != false)
            return false;
        return runtime.Approval?.TrySetResult(decision.Allow) == true;
    }

    private async Task<bool> ApproveAsync(Runtime runtime, AgentTool tool, string arguments, long timeout, CancellationToken token)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (await synchronization.EnterAsync(runtime.State.ChatId, token))
        {
            runtime.PendingApproval = new ToolApproval(Guid.NewGuid(), tool.OriginalName, arguments, timeout);
            runtime.Approval = completion;
            runtime.State.Append("");
            await SaveAsync(runtime, await chats.GetAsync(runtime.State.ProjectId, runtime.State.ChatId, token), token);
        }
        try { return await completion.Task.WaitAsync(token); }
        finally
        {
            using var lease = await synchronization.EnterAsync(runtime.State.ChatId, CancellationToken.None);
            runtime.PendingApproval = null;
            runtime.Approval = null;
        }
    }

    private async Task PersistToolMessageAsync(Runtime runtime, ChatCompletionMessage message, CancellationToken token)
    {
        using var lease = await synchronization.EnterAsync(runtime.State.ChatId, token);
        var chat = await chats.GetAsync(runtime.State.ProjectId, runtime.State.ChatId, token)
            ?? throw new InvalidOperationException("Chat not found.");
        var id = Guid.NewGuid();
        chat = await chats.AppendMessageCoreAsync(chat.ProjectId, chat.Id,
            new AppendChatMessageRequest(id, runtime.ToolHead, message.Role, message.Content, chat.Revision,
                BranchId: runtime.State.BranchId, ToolCalls: message.ToolCalls, ToolCallId: message.ToolCallId), token)
            ?? throw new InvalidOperationException("Tool history conflict.");
        runtime.ToolHead = id;
        if (runtime.State.Status == RunStatus.Generating) runtime.State.Start();
        await SaveAsync(runtime, chat, token);
    }

    private static Guid ResumeHead(ChatDetails chat, Guid branchId, Guid userId)
    {
        var head = chat.Branches?.SingleOrDefault(branch => branch.Id == branchId)?.HeadMessageId;
        if (head is null) return userId;
        var byId = chat.Messages.ToDictionary(message => message.Id);
        var visited = new HashSet<Guid>();
        var cursor = head;
        while (cursor is { } id && visited.Add(id) && byId.TryGetValue(id, out var message))
        {
            if (id == userId) return head.Value;
            if (message.Role == "User") break;
            cursor = message.ParentId;
        }
        return userId;
    }

    private static Guid ReplyId(Guid messageId)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"assistant:{messageId:N}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private async Task SaveAsync(Runtime runtime, ChatDetails? chat, CancellationToken token)
    {
        await repository.SaveAsync(runtime.State, token);
        runtime.Snapshot = Snapshot(runtime.State, chat) with { PendingApproval = runtime.PendingApproval, ActiveTool = runtime.ActiveTool };
        if (runtime is { ActiveMessageId: { } active, State.Status: RunStatus.Generating })
            runtime.Snapshot = runtime.Snapshot with { Queue = runtime.Snapshot.Queue.Where(item => item.Id != active).ToArray() };
        if (chat is not null)
            foreach (var other in _runtimes.Values.Where(item => item.State.ChatId == chat.Id && item != runtime))
                other.Snapshot = other.Snapshot with { ChatRevision = chat.Revision,
                    HeadMessageId = chat.Branches?.SingleOrDefault(branch => branch.Id == other.State.BranchId)?.HeadMessageId };
        Publish();
    }

    public async Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        foreach (var chatId in _runtimes.Keys.Where(key => key.ProjectId == projectId).Select(key => key.ChatId).Distinct())
            await RemoveAsync(projectId, chatId, null, cancellationToken);
        await repository.DeleteProjectAsync(projectId, cancellationToken);
    }

    public async Task<ChatDeleteResult> DeleteChatAsync(Guid projectId, Guid chatId, long revision, CancellationToken cancellationToken)
    {
        if (!_maintenance.TryAdd(chatId, 0)) throw new InvalidOperationException("Chat is being changed.");
        try
        {
            var chat = await chats.GetAsync(projectId, chatId, cancellationToken);
            if (chat is null) return new ChatDeleteResult(false, 0);
            if (chat.Revision != revision) return new ChatDeleteResult(false, chat.Revision);
            await PauseWorkersAsync(projectId, chatId, cancellationToken);
            var result = await chats.DeleteAsync(projectId, chatId, revision, cancellationToken);
            if (result.IsDeleted) await RemoveAsync(projectId, chatId, null, CancellationToken.None);
            return result;
        }
        finally { _maintenance.TryRemove(chatId, out _); }
    }

    public async Task<ChatBranchDeleteResult> DeleteBranchAsync(Guid projectId, Guid chatId, Guid branchId, long revision, CancellationToken cancellationToken)
    {
        if (!_maintenance.TryAdd(chatId, 0)) throw new InvalidOperationException("Chat is being changed.");
        try
        {
            var chat = await chats.GetAsync(projectId, chatId, cancellationToken);
            if (chat is null) return new ChatBranchDeleteResult(false, 0, null);
            if (chat.Revision != revision) return new ChatBranchDeleteResult(false, chat.Revision, null);
            await PauseWorkersAsync(projectId, chatId, cancellationToken);
            var result = await chats.DeleteBranchAsync(projectId, chatId, branchId, revision, cancellationToken);
            if (!result.IsDeleted)
            {
                return result;
            }

            var latest = await chats.GetAsync(projectId, chatId, CancellationToken.None);
            var retained = ChatBranchIds.Get(latest!);
            foreach (var runtime in _runtimes.Values.Where(item => item.State.ChatId == chatId && !retained.Contains(item.State.BranchId)))
                runtime.State.Clear();
            await RemoveAsync(projectId, chatId, retained, CancellationToken.None);
            return result;
        }
        finally { _maintenance.TryRemove(chatId, out _); }
    }

    public async Task<ProjectDeleteResult> DeleteProjectAsync(Guid projectId, long revision, CancellationToken cancellationToken)
    {
        if (!_deletingProjects.TryAdd(projectId, 0)) throw new InvalidOperationException("Project is being changed.");
        try
        {
            var project = await projects.GetAsync(projectId, cancellationToken);
            if (project is null) return ProjectDeleteResult.NotFound();
            if (project.Revision != revision) return ProjectDeleteResult.Conflict(project.Revision);
            foreach (var chat in await chats.ListAsync(projectId, cancellationToken))
                await PauseWorkersAsync(projectId, chat.Id, cancellationToken);
            var result = await projects.DeleteAsync(projectId, revision, cancellationToken);
            if (result.IsDeleted) await DeleteProjectAsync(projectId, CancellationToken.None);
            return result;
        }
        finally { _deletingProjects.TryRemove(projectId, out _); }
    }

    private async Task PauseWorkersAsync(Guid projectId, Guid chatId, CancellationToken token)
    {
        Task[] workers;
        using (await synchronization.EnterAsync(chatId, token))
        {
            var runtimes = _runtimes.Values.Where(item => item.State.ProjectId == projectId && item.State.ChatId == chatId).ToArray();
            foreach (var runtime in runtimes)
            {
                runtime.ResumeRequested = false;
                if (runtime.Cancellation is { } cancellation)
                {
                    await cancellation.CancelAsync();
                }

                if (runtime.Worker is not null) runtime.State.Pause();
            }
            workers = runtimes.Select(runtime => runtime.Worker ?? Task.CompletedTask).ToArray();
        }
        await Task.WhenAll(workers).WaitAsync(token);
    }
    public Task DeleteChatAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken) => RemoveAsync(projectId, chatId, null, cancellationToken);
    public Task ReconcileChatAsync(Guid projectId, Guid chatId, IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken) => RemoveAsync(projectId, chatId, branchIds, cancellationToken);

    private async Task RemoveAsync(Guid projectId, Guid chatId, IReadOnlySet<Guid>? retained, CancellationToken token)
    {
        Runtime[] removed;
        using (await synchronization.EnterAsync(chatId, token))
        {
            removed = _runtimes.Where(item => item.Key.ProjectId == projectId && item.Key.ChatId == chatId
                && (retained is null || !retained.Contains(item.Key.BranchId) && item.Value.State.Queue.Count == 0)).Select(item => item.Value).ToArray();
            foreach (var runtime in removed)
            {
                // ReSharper disable once InvertIf
                if (runtime.Cancellation is { } cancellation)
                {
                    await cancellation.CancelAsync();
                    runtime.State.Pause();
                }
            }
        }
        await Task.WhenAll(removed.Select(runtime => runtime.Worker ?? Task.CompletedTask));
        using (await synchronization.EnterAsync(chatId, token))
        {
            foreach (var runtime in removed) _runtimes.TryRemove(new RunKey(projectId, chatId, runtime.State.BranchId), out _);
            if (retained is null) await repository.DeleteChatAsync(projectId, chatId, token);
            else await repository.DeleteExceptAsync(projectId, chatId,
                _runtimes.Keys.Where(key => key.ProjectId == projectId && key.ChatId == chatId).Select(key => key.BranchId).ToHashSet(), token);
            Publish();
        }
    }

    private void Publish()
    {
        lock (_publicationLock)
        {
            var snapshot = _runtimes.Values.Select(runtime => runtime.Snapshot).ToArray();
            foreach (var subscriber in _subscribers.Values) subscriber.Writer.TryWrite(snapshot);
        }
    }

    private static ChatRunSnapshot Snapshot(ChatRunState state, ChatDetails? chat) => new(state.ProjectId, state.ChatId, state.BranchId,
        (ChatRunStatus)state.Status, state.StreamingContent,
        state.Queue.Select(item => new QueuedChatMessage(item.Id, item.Content, item.CreatedAt, item.ParentMessageId)).ToArray(),
        state.HasUnreadResponse, state.Error, state.Revision, chat?.Revision ?? 0,
        chat?.Branches?.SingleOrDefault(branch => branch.Id == state.BranchId)?.HeadMessageId);

    private static ChatRunState Clone(ChatRunState state) => ChatRunState.Restore(state.ProjectId, state.ChatId, state.BranchId,
        state.Status, state.StreamingContent, state.Error, state.HasUnreadResponse, state.Revision, state.Queue.ToArray(), state.Operations.ToArray());

    private sealed class Runtime(ChatRunState state)
    {
        public ChatRunState State { get; set; } = state;
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public ChatRunSnapshot Snapshot { get; set; } = ChatRunDispatcher.Snapshot(state, null);
        public Guid? ToolHead { get; set; }
        public ToolApproval? PendingApproval { get; set; }
        public TaskCompletionSource<bool>? Approval { get; set; }
        public string? ActiveTool { get; set; }
        public Task? Worker { get; set; }
        public CancellationTokenSource? Cancellation { get; set; }
        public Guid? ActiveMessageId { get; set; }
        public bool ResumeRequested { get; set; }
        public DateTimeOffset LastPublished { get; set; }
    }

    private readonly record struct RunKey(Guid ProjectId, Guid ChatId, Guid BranchId);
}
