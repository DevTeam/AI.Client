// ReSharper disable UseCollectionExpression
namespace AI.Application.Runs;

using Chat;
using Tools;
using Chats;
using Projects;
using Settings;
using Contracts.Chats;
using Contracts.Runs;
using Contracts.Projects;
using Contracts.Settings;
using Contracts.Workspace;
using Workspace;
using AI.Application.Resources;
using AI.Application.Memory;
using AI.Application.Instructions;
using AI.Application.Skills;
using AI.Application.Usage;
using Contracts.Usage;
using Domain.Runs;
using Domain.Chats;
using System.Collections.Concurrent;
using System.Threading.Channels;

public sealed class ChatRunDispatcher(
    IChatRunRepository repository, IChatService chats, IChatMutations chatMutations, IProjectService projects,
    IGlobalSettingsRepository settings, IGlobalSettingsService globalSettings, IChatAgent agent,
    IGlobalSecretStore secretStore, IClock clock, IIdGenerator ids, IChatSynchronization synchronization,
    IWorkspaceChangeTracker workspace, IToolPolicyResolver policies,
    IChatContextBuilder contextBuilder, IChatBranchIds branchIds, IResourceService resources, IReviewService reviews,
    IResourceModelProjection resourceProjection, IMemoryService memory, IProjectInstructionsService projectInstructions,
    ISkillRunner skillRunner, ISkillCatalog skillCatalog, IChatReplySuggestions replySuggestions,
    IToolAutoApprover autoApprover, ITokenUsageMeter usageMeter, ITokenUsageAggregator usageAggregator,
    IHistoryCheckpointService historyCheckpoints, IConnectionChoice connectionChoice,
    IChatKindPolicyRegistry kindPolicies, IModelMessageHeader headers, ITeamStatusBrief teamStatus)
    : IChatRunDispatcher, IUserPromptBroker, IAsyncDisposable
{
    private const int RecentMessageCapacity = 8;
    /// <summary>
    /// How often a waiting confirmation re-reads the standing policy. Human-scale waiting, so the
    /// cost is negligible and it catches a grant made anywhere — the card, the settings screen, or
    /// a tool — without every writer having to know that a prompt is open.
    /// </summary>
    private static readonly TimeSpan PolicyRecheck = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<RunKey, Runtime> _runtimes = new();
    private readonly Dictionary<Guid, Task> _titleTasks = [];
    private readonly Lock _titleTasksGate = new();
    private readonly ConcurrentDictionary<Guid, Channel<IReadOnlyList<ChatRunSnapshot>>> _subscribers = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Lock _publicationLock = new();
    private readonly ConcurrentDictionary<Guid, byte> _maintenance = new();
    private readonly ConcurrentDictionary<Guid, byte> _deletingProjects = new();
    private readonly Lock _updateGate = new();
    private volatile bool _updating;

    public bool TryEnterUpdateMaintenance()
    {
        lock (_updateGate)
        lock (_titleTasksGate)
        {
            if (_runtimes.Values.Any(runtime => runtime.Worker is { IsCompleted: false }
                    || runtime.Snapshot.Status == ChatRunStatus.Generating
                    || runtime.Snapshot.PendingApproval is not null || runtime.Snapshot.PendingPrompt is not null)
                || _titleTasks.Values.Any(task => !task.IsCompleted)) return false;
            _updating = true;
            return true;
        }
    }

    public void LeaveUpdateMaintenance()
    {
        lock (_updateGate) _updating = false;
        foreach (var runtime in _runtimes.Values) StartWorker(runtime);
    }

    public async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        var loadedChats = new Dictionary<(Guid ProjectId, Guid ChatId), ChatDetails?>();
        foreach (var state in await repository.ListAsync(cancellationToken))
        {
            using var lease = await synchronization.EnterAsync(state.ChatId, cancellationToken);
            var key = (state.ProjectId, state.ChatId);
            if (!loadedChats.TryGetValue(key, out var chat))
            {
                chat = await chats.GetAsync(state.ProjectId, state.ChatId, cancellationToken);
                loadedChats[key] = chat;
            }
            if (chat is null) continue;
            if (kindPolicies.TryResolve(new ChatKind(chat.Kind)) is null) continue;
            state.RecoverAfterRestart();
            await repository.SaveAsync(state, cancellationToken);
            var runtime = new Runtime(state)
            {
                Snapshot = Snapshot(state, chat)
            };
            _runtimes.TryAdd(new RunKey(state.ProjectId, state.ChatId, state.BranchId), runtime);
        }

        // Older builds left replacement siblings behind after moving the branch head. Once every
        // persisted run has been restored, its queue gives us the complete set of anchors that must
        // survive; anything else no branch reaches is abandoned history and can be collected.
        foreach (var ((projectId, chatId), loadedChat) in loadedChats)
        {
            if (loadedChat is null) continue;
            if (kindPolicies.TryResolve(new ChatKind(loadedChat.Kind)) is null) continue;
            using var lease = await synchronization.EnterAsync(chatId, cancellationToken);
            var pruned = await chatMutations.PruneMessagesCoreAsync(projectId, chatId,
                RetainedMessageIds(chatId), cancellationToken);
            if (pruned is null || pruned.Revision == loadedChat.Revision) continue;
            foreach (var runtime in _runtimes.Values.Where(item => item.State.ChatId == chatId))
                runtime.Snapshot = Snapshot(runtime.State, pruned);
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
        Task[] titleTasks;
        lock (_titleTasksGate) titleTasks = _titleTasks.Values.ToArray();
        await Task.WhenAll(_runtimes.Values.Select(runtime => runtime.Worker ?? Task.CompletedTask).Concat(titleTasks))
            .WaitAsync(cancellationToken);
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
        var id = ids.Create();
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

    public Task<ChatRunSnapshot> SubmitAsync(Guid projectId, Guid chatId, SubmitChatMessageRequest request,
        CancellationToken cancellationToken) => SubmitCoreAsync(projectId, chatId, request, true, cancellationToken);

    public Task<ChatRunSnapshot> SubmitUnattendedAsync(Guid projectId, Guid chatId, SubmitChatMessageRequest request,
        CancellationToken cancellationToken) => SubmitCoreAsync(projectId, chatId, request, false, cancellationToken);

    public Task<ChatRunSnapshot> SubmitFromRunAsync(Guid projectId, Guid chatId, SubmitChatMessageRequest request,
        ChatMessageSender sender, CancellationToken cancellationToken) =>
        SubmitCoreAsync(projectId, chatId, request, true, cancellationToken, sender);

    private async Task<ChatRunSnapshot> SubmitCoreAsync(Guid projectId, Guid chatId, SubmitChatMessageRequest request,
        bool interactive, CancellationToken cancellationToken, ChatMessageSender? sender = null)
    {
        if (_updating) throw new InvalidOperationException("The application is restarting to install an update. Try again after it reconnects.");
        _shutdown.Token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.Content) && request.Resources is not { Count: > 0 })
            throw new ArgumentException("A message needs text or a resource reference.");
        if (request.OperationId == Guid.Empty || request.MessageId == Guid.Empty || !Enum.IsDefined(request.Mode))
            throw new ArgumentException("Valid operation, message and submission mode are required.");
        // Interrupting has to finish before the queue is rewritten: the worker commits the
        // truncated answer and releases the active command on its way out, and rewriting the
        // queue underneath it would race both.
        if (request.Mode is ChatSubmitMode.SendNow or ChatSubmitMode.Replace)
            await InterruptBranchAsync(projectId, chatId, request.BranchId ?? chatId, request.OperationId, cancellationToken);
        using var lease = await synchronization.EnterAsync(chatId, cancellationToken);
        if (_maintenance.ContainsKey(chatId) || _deletingProjects.ContainsKey(projectId)) throw new InvalidOperationException("Chat is being changed.");
        _ = await projects.GetAsync(projectId, cancellationToken) ?? throw new InvalidOperationException("Project not found.");
        var chat = await chats.GetAsync(projectId, chatId, cancellationToken) ?? throw new InvalidOperationException("Chat not found.");
        _ = kindPolicies.Resolve(new ChatKind(chat.Kind));
        var branchId = request.Mode == ChatSubmitMode.Fork ? request.MessageId : request.BranchId ?? chat.Id;
        var sourceBranchId = request.BranchId ?? chat.Id;
        var sourceBranch = chat.Branches?.SingleOrDefault(branch => branch.Id == sourceBranchId)
            ?? throw new ArgumentException("Branch does not exist.");
        var runtime = await GetRuntimeAsync(projectId, chatId, branchId, chat, cancellationToken);
        // An id repeated with the same message is a retry and changes nothing; repeated with another
        // message it is a model reusing a made-up id, and returning the old result as if the new
        // message had been accepted would lose it without a word.
        var known = (chat.Messages.SingleOrDefault(message => message.Id == request.MessageId)?.Content
            ?? runtime.State.Queue.SingleOrDefault(item => item.Id == request.MessageId)?.Content)?.Trim();
        if (runtime.State.Operations.Contains(request.OperationId))
        {
            if (known is not null && known != request.Content.Trim())
                throw new ArgumentException("This operationId was already used for a different message. Use a fresh random UUID for every new message.");
            return runtime.Snapshot;
        }
        if (known is not null)
            throw new ArgumentException("This message id is already used in the chat. Use a fresh random UUID for every new message.");
        var validatedResources = await resources.ValidateForChatAsync(projectId, chatId, request.Resources, cancellationToken);
        // The branch of an aside moves under it by design — a running turn is what it is meant for —
        // so neither the revision nor a parent applies to it.
        if (request.Mode == ChatSubmitMode.Aside)
            return await SubmitAsideAsync(runtime, chat, sourceBranch, request.OperationId, new QueuedRunMessage(
                request.MessageId, request.Content.Trim(), clock.UtcNow,
                Resources: ResourceReferences.ToDomain(validatedResources), Interactive: interactive,
                IsAside: true, Sender: sender), cancellationToken);
        if (request.ExpectedBranchRevision is { } branchRevision && branchRevision != sourceBranch.Revision
            && request.Mode != ChatSubmitMode.Replace)
            throw new InvalidOperationException("The branch changed. Reload it before submitting.");
        if (request.ParentMessageId is { } parent && chat.Messages.All(message => message.Id != parent))
            throw new ArgumentException("Parent message does not exist.");
        // Refused here, where the caller can still choose another name, rather than when the worker
        // makes the branch and nobody is left to correct it.
        if (request is { Mode: ChatSubmitMode.Fork, BranchMember: { } newMember })
        {
            if (string.IsNullOrWhiteSpace(newMember.Name) || string.IsNullOrWhiteSpace(newMember.Role)
                || newMember.Name.Trim().Length > 32 || newMember.Role.Trim().Length > 48)
                throw new ArgumentException("A teammate needs a name of up to 32 characters and a role of up to 48.");
            if (chat.Branches?.Any(branch => string.Equals(branch.Member?.Name, newMember.Name.Trim(),
                    StringComparison.OrdinalIgnoreCase)) == true
                // A fork submitted a moment ago has no branch yet, only its queued command.
                || _runtimes.Values.Where(item => item.State.ChatId == chatId).SelectMany(item => item.State.Queue)
                    .Any(item => string.Equals(item.BranchMember?.Name, newMember.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"The team already has a teammate named '{newMember.Name.Trim()}'.");
        }
        if (request.Mode != ChatSubmitMode.Fork && chat.Branches?.All(branch => branch.Id != branchId) == true
            && runtime.State.Queue.Count == 0 && branchId != request.MessageId)
            throw new ArgumentException("Branch does not exist.");
        if (request.Mode is ChatSubmitMode.SendNow or ChatSubmitMode.Replace && runtime.State.Status == RunStatus.Generating)
            throw new InvalidOperationException("The branch started generating again. Send the message once more.");
        Guid? replaceId = null;
        var parentId = request.ParentMessageId;
        var parentMode = request.ParentMode switch
        {
            Contracts.Runs.MessageParentMode.Root => Domain.Runs.MessageParentMode.Root,
            Contracts.Runs.MessageParentMode.Explicit => Domain.Runs.MessageParentMode.Explicit,
            _ => Domain.Runs.MessageParentMode.BranchHead
        };
        if (request.Mode == ChatSubmitMode.Replace)
        {
            var source = chat.Messages.SingleOrDefault(message => message.Id == request.ReplaceSourceId)
                ?? throw new ArgumentException("Replacement message does not exist.");
            if (!IsAncestor(chat, sourceBranch.HeadMessageId, source.Id))
                throw new ArgumentException("Replacement message does not belong to the selected branch.");
            replaceId = source.Id;
            parentId = source.ParentId;
            parentMode = parentId is null ? Domain.Runs.MessageParentMode.Root : Domain.Runs.MessageParentMode.Explicit;
        }
        else if (request.Mode == ChatSubmitMode.Fork)
        {
            if (parentMode == Domain.Runs.MessageParentMode.Root)
            {
                parentId = null;
            }
            else
            {
                parentMode = Domain.Runs.MessageParentMode.Explicit;
                parentId ??= sourceBranch.HeadMessageId;
                if (parentId is null || !IsAncestor(chat, sourceBranch.HeadMessageId, parentId.Value))
                    throw new ArgumentException("Fork parent does not belong to the selected branch.");
            }
        }
        var before = Clone(runtime.State);
        // Replacement abandons the selected branch from the source message onward. Keeping a
        // committed command or prepared rows from that abandoned tail would either resume the old
        // answer first or later execute messages against a context that no longer exists.
        if (request.Mode == ChatSubmitMode.Replace) runtime.State.Clear();
        runtime.State.Enqueue(request.OperationId, new QueuedRunMessage(request.MessageId, request.Content.Trim(), clock.UtcNow,
            parentMode, parentId, replaceId, request.Mode == ChatSubmitMode.Fork ? sourceBranchId : null,
            sourceBranch.Revision, Resources: ResourceReferences.ToDomain(validatedResources), Interactive: interactive,
            Sender: sender, BranchTitle: request.Mode == ChatSubmitMode.Fork ? request.BranchTitle : null,
            BranchMember: request is { Mode: ChatSubmitMode.Fork, BranchMember: { } member }
                ? new ChatBranchMember(member.Name, member.Role, string.Empty) : null,
            // A teammate's report or question waits for no one: a lead busy with a turn reads it
            // there, instead of polling for it and then spending a whole turn on it afterwards.
            JoinsTurn: sender is not null && request.Mode == ChatSubmitMode.Send));
        if (request.Mode == ChatSubmitMode.Queue)
        {
            runtime.ResumeRequested = false;
            if (runtime.Cancellation is { } cancellation)
            {
                await cancellation.CancelAsync();
            }

            runtime.State.Pause();
        }
        else if (request.Mode == ChatSubmitMode.SendNow)
        {
            // The interrupted command is abandoned, not kept for a retry: the user answered the
            // question of what to do with it by typing something else and asking for it now.
            runtime.ResumeRequested = false;
            runtime.State.DropCommitted();
            runtime.State.Move(request.MessageId, 0);
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

    /// <summary>
    /// An aside never starts a turn. With a command in flight — generating, or stopped with its
    /// user message already in the transcript — it waits for that turn's next step boundary;
    /// otherwise there is no turn to join and it is appended at once.
    /// </summary>
    private async Task<ChatRunSnapshot> SubmitAsideAsync(Runtime runtime, ChatDetails chat, ChatBranchView branch,
        Guid operationId, QueuedRunMessage aside, CancellationToken token)
    {
        var before = Clone(runtime.State);
        try
        {
            if (runtime.State.Status == RunStatus.Generating
                || runtime.State.Queue.Any(item => item.Stage == QueuedRunStage.UserCommitted))
            {
                runtime.State.Enqueue(operationId, aside);
            }
            else
            {
                runtime.State.RememberOperation(operationId);
                chat = await AppendAsidesAsync(runtime, chat, [aside], branch.HeadMessageId, ChatMessageDelivery.Aside, token);
            }
            await SaveAsync(runtime, chat, token);
        }
        catch { runtime.State = before; throw; }
        return runtime.Snapshot;
    }

    /// <summary>
    /// Writes asides as user messages under <paramref name="parent"/>, in the order they were
    /// submitted, and drops each from the queue once it is in the transcript. A failure part-way
    /// leaves the rest queued rather than lost.
    /// </summary>
    private async Task<ChatDetails> AppendAsidesAsync(Runtime runtime, ChatDetails chat,
        IReadOnlyList<QueuedRunMessage> asides, Guid? parent, ChatMessageDelivery delivery, CancellationToken token)
    {
        foreach (var aside in asides)
        {
            var baseRevision = chat.Revision;
            chat = await chatMutations.AppendMessageCoreAsync(chat.ProjectId, chat.Id,
                new AppendChatMessageRequest(aside.Id, parent, "User", aside.Content, chat.Revision,
                    BranchId: runtime.State.BranchId, Resources: ResourceReferences.ToContract(aside.Resources),
                    Delivery: (MessageDelivery)delivery, Sender: ToContract(aside.Sender)),
                RetainedMessageIds(chat.Id), token)
                ?? throw new InvalidOperationException("Message conflict.");
            TrackMessage(runtime, baseRevision, chat, aside.Id);
            runtime.State.Remove(aside.Id);
            parent = aside.Id;
        }
        return chat;
    }

    /// <summary>
    /// Hands the running turn the asides and team messages that arrived since its last step. Called
    /// by the agent after a tool batch's results, the only point where a user message keeps the
    /// history valid.
    /// </summary>
    private async Task<IReadOnlyList<ChatCompletionMessage>> TakeAsidesIntoTurnAsync(Runtime runtime,
        CancellationToken token)
    {
        using var lease = await synchronization.EnterAsync(runtime.State.ChatId, token);
        var asides = runtime.State.TurnJoiners;
        if (asides.Count == 0 || runtime.ToolHead is not { } head) return [];
        var chat = await chats.GetAsync(runtime.State.ProjectId, runtime.State.ChatId, token)
            ?? throw new InvalidOperationException("Chat not found.");
        chat = await AppendAsidesAsync(runtime, chat, asides, head, ChatMessageDelivery.InTurn, token);
        runtime.ToolHead = asides[^1].Id;
        await SaveAsync(runtime, chat, token);
        var taken = new List<ChatCompletionMessage>();
        foreach (var aside in asides)
        {
            var message = chat.Messages.Single(item => item.Id == aside.Id);
            var projected = await resourceProjection.ProjectAsync(chat.ProjectId, chat.Id, message.Content,
                message.Resources, token);
            taken.Add(new ChatCompletionMessage("user", message.Content,
                ModelContent: headers.Apply(message, chat, projected), MessageId: message.Id,
                ImageAssetIds: message.Resources?.Where(item => item.Kind == AI.Contracts.Resources.ChatResourceKind.Image)
                    .Select(item => item.AssetId).OfType<string>().ToArray()));
        }
        return taken;
    }

    /// <summary>
    /// The asides an attempt took, from its user message to <paramref name="head"/>, as they were
    /// queued: a retry prunes that attempt, and what the person added must not go with it.
    /// </summary>
    private static List<QueuedRunMessage> TakenAsides(ChatDetails chat, Guid? head, Guid userId)
    {
        var byId = chat.Messages.ToDictionary(message => message.Id);
        var taken = new List<QueuedRunMessage>();
        var cursor = head;
        while (cursor is { } id && id != userId && byId.TryGetValue(id, out var message))
        {
            if (message.Delivery == MessageDelivery.InTurn)
            {
                // A team message that asks for something joined the turn instead of waiting for its
                // own; on a retry it must still get one if the new attempt ends before taking it.
                var joins = message.Sender?.Intent is "question" or "answer" or "blocker" or "done";
                taken.Add(new QueuedRunMessage(message.Id, message.Content, message.CreatedAt,
                    Resources: ResourceReferences.ToDomain(message.Resources), IsAside: !joins,
                    Sender: message.Sender is { } sender ? new ChatMessageSender(sender.ChatId, sender.BranchId, sender.Intent) : null,
                    JoinsTurn: joins));
            }
            cursor = message.ParentId;
        }
        taken.Reverse();
        return taken;
    }

    /// <summary>
    /// A teammate whose turn stopped — for lack of progress, or failed — tells the lead, who would
    /// otherwise learn of it only by looking, and a team waits on a lead that does not look. A
    /// teammate's branch is one that a team message of the main branch
    /// started (docs/34-asides-and-team-messages.md); any other branch keeps the report to itself.
    /// </summary>
    private async Task ReportToLeadAsync(Runtime runtime, string report, CancellationToken token)
    {
        var (projectId, chatId, branchId) = (runtime.State.ProjectId, runtime.State.ChatId, runtime.State.BranchId);
        if (branchId == chatId) return;
        var chat = await chats.GetAsync(projectId, chatId, token);
        var root = chat?.Branches?.SingleOrDefault(branch => branch.Id == branchId)?.RootMessageId is { } rootId
            ? chat.Messages.SingleOrDefault(message => message.Id == rootId)
            : null;
        if (root?.Sender is not { Intent.Length: > 0 } brief || brief.ChatId != chatId || brief.BranchId != chatId) return;
        var id = ids.Create();
        // The teammate's own answer is the report either way; a lead that cannot be told now is
        // no reason to fail the turn that is telling it.
        try
        {
            await SubmitCoreAsync(projectId, chatId, new SubmitChatMessageRequest(id, id,
                    report, ChatSubmitMode.Send, chatId),
                true, token, new ChatMessageSender(chatId, branchId, "blocker"));
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException
            or UnauthorizedAccessException or Domain.Common.DomainException) { }
    }

    private static MessageSender? ToContract(ChatMessageSender? sender) =>
        sender is null ? null : new MessageSender(sender.ChatId, sender.BranchId, sender.Intent);

    public Task<ChatRunSnapshot?> StopAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken, Guid? operationId = null) =>
        MutateAsync(projectId, chatId, branchId, runtime => { runtime.ResumeRequested = false; runtime.Cancellation?.Cancel(); runtime.State.Pause(); }, operationId, cancellationToken);
    public Task<ChatRunSnapshot?> MarkReadAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken, Guid? operationId = null) =>
        MutateAsync(projectId, chatId, branchId, runtime => runtime.State.MarkRead(), operationId, cancellationToken);
    public Task<ChatRunSnapshot?> ResumeAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken, Guid? operationId = null) =>
        MutateAsync(projectId, chatId, branchId, runtime => { runtime.ResumeRequested = true; runtime.State.Resume(); }, operationId, cancellationToken);
    public Task<ChatRunSnapshot?> SkipFailedAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken, Guid? operationId = null) =>
        MutateAsync(projectId, chatId, branchId, runtime => runtime.State.SkipFailed(), operationId, cancellationToken);
    public Task<ChatRunSnapshot?> RebaseAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken, Guid? operationId = null) =>
        MutateAsync(projectId, chatId, branchId, (runtime, chat) =>
        {
            if (runtime.State.Queue.Count == 0) return;
            var queued = runtime.State.Queue[0];
            var anchorBranchId = queued.ParentBranchId ?? branchId;
            var anchor = chat.Branches?.SingleOrDefault(item => item.Id == anchorBranchId)
                ?? throw new InvalidOperationException("The branch required to rebase this message no longer exists.");
            runtime.State.RebaseFirst(anchor.Revision, anchor.HeadMessageId);
        }, operationId, cancellationToken);
    /// <summary>
    /// Removes every command that has not been sent yet and nothing else. It used to depend on
    /// whether a worker happened to be unwinding, so the same click removed a different number of
    /// messages depending on timing; now it removes exactly the rows the queue panel shows.
    /// </summary>
    public Task<ChatRunSnapshot?> ClearAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken, Guid? operationId = null) =>
        MutateAsync(projectId, chatId, branchId, runtime => runtime.State.ClearPending(), operationId, cancellationToken);

    /// <summary>
    /// Abandons the command the run is working from, or stopped on, and lets the rest of the
    /// queue continue. What the transcript keeps is the user message and whatever truncated
    /// answer the attempt produced; what it does not keep is a run stuck on a command nobody
    /// intends to retry.
    /// </summary>
    public async Task<ChatRunSnapshot?> DiscardAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken, Guid? operationId = null)
    {
        await InterruptBranchAsync(projectId, chatId, branchId, operationId ?? Guid.Empty, cancellationToken);
        return await MutateAsync(projectId, chatId, branchId, runtime => runtime.State.DropCommitted(), operationId, cancellationToken);
    }

    /// <summary>
    /// Stops the branch and empties the queue, the command in flight included. The destructive
    /// counterpart to <see cref="ClearAsync"/>, asked for explicitly.
    /// </summary>
    public async Task<ChatRunSnapshot?> ClearAllAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken, Guid? operationId = null)
    {
        await InterruptBranchAsync(projectId, chatId, branchId, operationId ?? Guid.Empty, cancellationToken);
        return await MutateAsync(projectId, chatId, branchId, runtime => runtime.State.Clear(), operationId, cancellationToken);
    }
    public Task<ChatRunSnapshot?> UpdateQueuedAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, UpdateQueuedMessageRequest request, CancellationToken cancellationToken) =>
        MutateAsync(projectId, chatId, branchId, runtime =>
        {
            if (runtime.ActiveMessageId == messageId) throw new InvalidOperationException("This message has already been sent, so it can no longer be edited.");
            if (request.Content is not null) runtime.State.Update(messageId, request.Content);
            if (request.Position is { } position)
                runtime.State.Move(messageId, Math.Max(runtime.State.Queue.Count(item => item.Stage == QueuedRunStage.UserCommitted), position));
        }, request.OperationId, cancellationToken);
    public async Task<ChatRunSnapshot?> RemoveQueuedAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, CancellationToken cancellationToken, Guid? operationId = null)
    {
        // The browser may still show a Prepared snapshot while the worker has already claimed
        // the row. Honour the click against that exact message instead of returning 409: stop it,
        // wait until its partial answer/tool history is settled, then drop its committed command.
        // A different active message is never interrupted by this targeted operation.
        await InterruptMessageAsync(projectId, chatId, branchId, messageId, operationId ?? Guid.Empty, cancellationToken);
        return await MutateAsync(projectId, chatId, branchId, runtime =>
        {
            var queued = runtime.State.Queue.FirstOrDefault(item => item.Id == messageId);
            if (queued is null) return;
            if (queued.Stage == QueuedRunStage.UserCommitted)
                runtime.State.DropCommitted();
            else if (runtime.State.Status == RunStatus.Failed && runtime.State.Queue.Count > 0 && runtime.State.Queue[0].Id == messageId)
                runtime.State.SkipFailed();
            else
                runtime.State.Remove(messageId);
        }, operationId, cancellationToken);
    }

    /// <summary>
    /// Abandons the command currently being generated and immediately starts the selected waiting
    /// command. Other prepared commands keep their relative order behind it.
    /// </summary>
    public async Task<ChatRunSnapshot?> SendQueuedNowAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId,
        CancellationToken cancellationToken, Guid? operationId = null)
    {
        await InterruptBranchAsync(projectId, chatId, branchId, operationId ?? Guid.Empty, cancellationToken);
        return await MutateAsync(projectId, chatId, branchId, runtime =>
        {
            if (runtime.State.Queue.All(item => item.Id != messageId || item.Stage != QueuedRunStage.Prepared))
                throw new InvalidOperationException("This message is no longer waiting in the queue.");
            runtime.State.DropCommitted();
            runtime.State.Move(messageId, 0);
        }, operationId, cancellationToken);
    }

    private Task<ChatRunSnapshot?> MutateAsync(Guid projectId, Guid chatId, Guid branchId, Action<Runtime> mutate,
        Guid? operationId, CancellationToken token) =>
        MutateAsync(projectId, chatId, branchId, (runtime, _) => mutate(runtime), operationId, token);

    private async Task<ChatRunSnapshot?> MutateAsync(Guid projectId, Guid chatId, Guid branchId,
        Action<Runtime, ChatDetails> mutate, Guid? operationId, CancellationToken token)
    {
        using var lease = await synchronization.EnterAsync(chatId, token);
        if (_maintenance.ContainsKey(chatId) || _deletingProjects.ContainsKey(projectId)) throw new InvalidOperationException("Chat is being changed.");
        var chat = await chats.GetAsync(projectId, chatId, token);
        if (chat is null) return null;
        var runtime = await GetRuntimeAsync(projectId, chatId, branchId, chat, token);
        if (operationId is { } duplicate && runtime.State.Operations.Contains(duplicate)) return runtime.Snapshot;
        var before = Clone(runtime.State);
        try { if (operationId is { } id) runtime.State.RememberOperation(id); mutate(runtime, chat); await SaveAsync(runtime, chat, token); }
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
        lock (_updateGate)
        {
            if (_updating) return;
            if (_shutdown.IsCancellationRequested || runtime.Worker is { IsCompleted: false }
                || runtime.State.Queue.Count == 0 || runtime.State.Status is RunStatus.Paused or RunStatus.Interrupted or RunStatus.Failed) return;
            runtime.Worker = Task.Run(() => ProcessAsync(runtime));
        }
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
                    if (queued.IsAside)
                    {
                        // Left behind by a command that was dropped before it reached a step
                        // boundary: no turn will take it now, so it joins the history as it is.
                        var current = await chats.GetAsync(runtime.State.ProjectId, runtime.State.ChatId, CancellationToken.None)
                            ?? throw new InvalidOperationException("Chat not found.");
                        current = await AppendAsidesAsync(runtime, current, runtime.State.Asides,
                            current.Branches?.SingleOrDefault(branch => branch.Id == runtime.State.BranchId)?.HeadMessageId,
                            ChatMessageDelivery.Aside, CancellationToken.None);
                        await SaveAsync(runtime, current, CancellationToken.None);
                        continue;
                    }
                    runtime.ActiveMessageId = queued.Id;
                    runtime.ResumeRequested = false;
                    runtime.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                    token = runtime.Cancellation.Token;
                    runtime.State.Start();
                    // A previous attempt at this same command may have been cut short, leaving a
                    // truncated answer and its tool messages on the branch. Retrying replaces that
                    // attempt rather than continuing from it, so the branch is rewound to the user
                    // message first and the abandoned tail pruned.
                    runtime.ToolHead = null;
                    var chat = await chats.GetAsync(runtime.State.ProjectId, runtime.State.ChatId, token) ?? throw new InvalidOperationException("Chat not found.");
                    if (chat.Messages.Any(message => message.Id == PartialReplyId(queued.Id))
                        && chat.Branches?.SingleOrDefault(branch => branch.Id == runtime.State.BranchId) is { } attemptBranch
                        && IsAncestor(chat, attemptBranch.HeadMessageId, queued.Id))
                    {
                        var taken = TakenAsides(chat, attemptBranch.HeadMessageId, queued.Id);
                        if (await chatMutations.RewindBranchCoreAsync(chat.ProjectId, chat.Id, runtime.State.BranchId,
                                queued.Id, RetainedMessageIds(chat.Id), token) is { } rewound)
                        {
                            runtime.State.ReturnAsides(taken);
                            chat = rewound;
                        }
                    }
                    if (chat.Messages.Any(message => message.Id == ReplyId(queued.Id)))
                    {
                        runtime.State.Remove(queued.Id);
                        runtime.State.Complete(true);
                        runtime.ActiveMessageId = null;
                        runtime.Cancellation.Dispose();
                        runtime.Cancellation = null;
                        chat = await chatMutations.PruneMessagesCoreAsync(chat.ProjectId, chat.Id,
                            RetainedMessageIds(chat.Id), token) ?? chat;
                        await SaveAsync(runtime, chat, token);
                        continue;
                    }
                    await SaveAsync(runtime, chat, token);
                    var project = await projects.GetAsync(chat.ProjectId, token) ?? throw new InvalidOperationException("Project not found.");
                    var global = await settings.LoadAsync(token);
                    // The chain honours a chat-level override first, then the project's
                    // connection, then the global default. A project left on 'Default' (no
                    // explicit connection) follows whatever the global default currently is, which
                    // is the point of the feature. A disabled link in the chain is passed over, as
                    // the composer passes over it: the chat runs on the model it shows.
                    var connection = connectionChoice.Choose(global.Connections, chat.ConnectionId, project.ConnectionId);
                    if (connection is null)
                    {
                        throw new InvalidOperationException("Choose an enabled connection for this chat.");
                    }
                    // Retaining the command until completion makes restart/resume safe: an already committed user message is reused.
                    if (chat.Messages.All(message => message.Id != queued.Id))
                    {
                        var parent = ResolveParent(chat, runtime.State.BranchId, queued);
                        var baseRevision = chat.Revision;
                        chat = await chatMutations.AppendMessageCoreAsync(chat.ProjectId, chat.Id,
                            new AppendChatMessageRequest(queued.Id, parent, "User", queued.Content, chat.Revision,
                                BranchId: runtime.State.BranchId, ParentBranchId: queued.ParentBranchId,
                                ReplaceSourceId: queued.ReplaceSourceId,
                                Resources: ResourceReferences.ToContract(queued.Resources), Sender: ToContract(queued.Sender),
                                BranchTitle: queued.BranchTitle,
                                BranchMember: queued.BranchMember is { } member ? new TeamMember(member.Name, member.Role) : null),
                            RetainedMessageIds(chat.Id, queued.Id), token)
                            ?? throw new InvalidOperationException("Message conflict.");
                        TrackMessage(runtime, baseRevision, chat, queued.Id);
                    }
                    runtime.State.MarkUserCommitted(queued.Id);
                    if (connection.ImageInput == AI.Contracts.Settings.ImageInputMode.Disabled &&
                        queued.Resources?.Any(item => item.Kind == AI.Domain.Resources.ChatResourceKind.Image) == true)
                        throw new InvalidOperationException("This connection is set to reject image input. Choose another connection or enable image input in its settings.");
                    request = new ChatCompletionRequest(connection.BaseUrl, connection.Model,
                        await secretStore.GetAsync("connection", connection.Id, token),
                        await resourceProjection.ProjectAsync(runtime.State.ProjectId, runtime.State.ChatId, queued.Content,
                            ResourceReferences.ToContract(queued.Resources), token), connection.Id,
                        // The branch's history as the model is to see it: in full, or from the
                        // summary of its deepest checkpoint on.
                        kindPolicies.Resolve(new ChatKind(chat.Kind)).Behavior.UseFullHistory
                            ? chat.Messages.Select(message => new ChatCompletionMessage(message.Role.ToLowerInvariant(),
                            message.Content, message.ToolCalls, message.ToolCallId,
                            ModelContent: message.Role == "User" ? headers.Apply(message, chat, message.Content) : null,
                            ImageAssetIds: message.Resources?.Where(item => item.Kind == AI.Contracts.Resources.ChatResourceKind.Image)
                                .Select(item => item.AssetId).OfType<string>().ToArray())).ToArray()
                            : await historyCheckpoints.ApplyAsync(chat.ProjectId, chat.Id,
                                await contextBuilder.BuildAsync(chat, ResumeHead(chat, runtime.State.BranchId, queued.Id), token), token),
                        Kind: new ChatKind(chat.Kind), KindState: chat.KindState,
                        KindStateVersion: chat.KindStateVersion, ProjectId: runtime.State.ProjectId,
                        TeamStatus: teamStatus.Describe(chat, runtime.State.BranchId, _runtimes.Values
                            .Where(item => item.State.ChatId == chat.Id).Select(item => item.Snapshot).ToArray()));
                    if (connection.ImageInput == AI.Contracts.Settings.ImageInputMode.Disabled)
                        request = request with { ContextMessages = request.ContextMessages?
                            .Select(item => item with { ImageAssetIds = null }).ToArray() };
                    runtime.ToolHead = ResumeHead(chat, runtime.State.BranchId, queued.Id);
                    await SaveAsync(runtime, chat, token);
                }

                // Every request below is this turn's, including those of its subtasks, its routing
                // and its compaction; the scope ends with the agent so the title and reply
                // suggestions started afterwards are the chat's rather than the turn's.
                runtime.TurnRecords.Clear();
                runtime.TurnUsage = null;
                WorkspaceChangeSet workspaceChanges;
                using (usageMeter.Begin(new TokenUsageScope(TokenUsagePurpose.Answer, runtime.State.ProjectId,
                           runtime.State.ChatId, runtime.State.BranchId, queued.Id,
                           (record, ct) => ReportTokenUsageAsync(runtime, record, ct))))
                workspaceChanges = await agent.RunAsync(runtime.State.ProjectId, runtime.State.ChatId, runtime.State.BranchId, request,
                    (message, ct) => PersistToolMessageAsync(runtime, message, ct),
                    async (content, ct) =>
                    {
                        using var lease = await synchronization.EnterAsync(runtime.State.ChatId, ct);
                        runtime.State.Append(content);
                        if (clock.UtcNow - runtime.LastPublished >= TimeSpan.FromMilliseconds(150))
                        {
                            runtime.LastPublished = clock.UtcNow;
                            // Streaming changes only the run state. Re-reading every immutable
                            // message node merely to preserve chat metadata made large chats hit
                            // storage several times per second.
                            await SaveAsync(runtime, null, ct);
                        }
                    },
                    (activity, ct) => ReportToolActivityAsync(runtime, activity, ct),
                    (wait, ct) => ReportTransportActivityAsync(runtime, wait, ct),
                    (tool, arguments, timeout, position, ct) => ApproveAsync(runtime, tool, arguments, timeout, position, ct), token,
                    interactive: queued.Interactive,
                    draft: (chunk, ct) => ReportDraftAsync(runtime, chunk, ct),
                    contextUsage: (usage, ct) => ReportContextAsync(runtime, usage, ct),
                    draftToolCall: (name, ct) => ReportDraftToolCallAsync(runtime, name, ct),
                    overlayPromptsAllowed: queued.Interactive,
                    asides: ct => TakeAsidesIntoTurnAsync(runtime, ct),
                    stalledReport: (report, ct) => ReportToLeadAsync(runtime,
                        "Stopped: several steps in a row produced nothing new.\n\n" + report, ct));
                var suggestTitle = false;
                Guid? answeredHead = null;
                using (await synchronization.EnterAsync(runtime.State.ChatId, token))
                {
                    token.ThrowIfCancellationRequested();
                    var chat = await chats.GetAsync(runtime.State.ProjectId, runtime.State.ChatId, token) ?? throw new InvalidOperationException("Chat not found.");
                    // Stable reply id avoids duplicate assistant messages if the process stops between chat and run commits.
                    var replyId = ReplyId(queued.Id);
                    if (chat.Messages.All(message => message.Id != replyId))
                    {
                        var baseRevision = chat.Revision;
                        chat = await chatMutations.AppendMessageCoreAsync(chat.ProjectId, chat.Id,
                            new AppendChatMessageRequest(replyId, runtime.ToolHead ?? queued.Id, "Assistant", runtime.State.StreamingContent, chat.Revision,
                                BranchId: runtime.State.BranchId,
                                WorkspaceChanges: workspaceChanges.IsEmpty ? null : workspaceChanges),
                            RetainedMessageIds(chat.Id), token)
                            ?? throw new InvalidOperationException("Response conflict.");
                        TrackMessage(runtime, baseRevision, chat, replyId);
                    }
                    runtime.State.Remove(queued.Id);
                    runtime.State.Complete(true);
                    // Asides that came after the turn's last step boundary: the model did not read
                    // them, and they join the history right after the reply, ahead of whatever
                    // command is queued next.
                    var asides = runtime.State.Asides;
                    if (asides.Count > 0)
                        chat = await AppendAsidesAsync(runtime, chat, asides, replyId, ChatMessageDelivery.Aside, token);
                    // The persisted reply now owns the final copy; keeping the live copy would
                    // duplicate it if the branch is later paused with another queued message.
                    runtime.WorkspaceChanges = null;
                    runtime.ActiveMessageId = null;
                    runtime.Cancellation.Dispose();
                    runtime.Cancellation = null;
                    chat = await chatMutations.PruneMessagesCoreAsync(chat.ProjectId, chat.Id,
                        RetainedMessageIds(chat.Id), token) ?? chat;
                    await SaveAsync(runtime, chat, token);
                    suggestTitle = runtime.State.BranchId == chat.Id && chat.AutoTitlePending
                        && chat.Messages.Count(message => message.Role == "User" && message.Delivery == MessageDelivery.Turn) == 1;
                    // A reply is drafted only for the answer the user is left with: with more
                    // messages queued behind it, the next one is already the reply, and an aside
                    // written under it is not an answer to reply to.
                    if (runtime.State.Queue.Count == 0 && asides.Count == 0 && kindPolicies.Resolve(new ChatKind(chat.Kind)).Behavior.SuggestReplies)
                        answeredHead = chat.Branches?.SingleOrDefault(branch => branch.Id == runtime.State.BranchId)?.HeadMessageId;
                }
                if (suggestTitle || answeredHead is not null)
                {
                    var automation = (await settings.LoadAsync(token)).ChatAutomation ?? new ChatAutomationSettings();
                    if (suggestTitle && automation.AutoTitle) StartTitleSkill(runtime.State.ProjectId, runtime.State.ChatId);
                    if (answeredHead is { } head && automation.SuggestReplies)
                        replySuggestions.Start(runtime.State.ProjectId, runtime.State.ChatId, runtime.State.BranchId, head);
                }
            }
        }
        catch (Exception error)
        {
            using var lease = await synchronization.EnterAsync(runtime.State.ChatId, CancellationToken.None);
            await CommitPartialAnswerAsync(runtime);
            // Clear can empty the queue while a cancelled worker is still unwinding. Keep the
            // Idle state established by Clear instead of changing the empty queue back to Paused.
            // A Host shutdown cancels the same token as Stop. Pausing here made a restart look as
            // if the user had stopped the run; it is an interruption, like a crash after restart.
            if (error is OperationCanceledException && _shutdown.IsCancellationRequested
                && runtime.State.Status == RunStatus.Generating)
            {
                runtime.State.Interrupt();
            }
            else if (error is OperationCanceledException)
            {
                if (runtime.State.Queue.Count > 0) runtime.State.Pause();
            }
            // Unlike a retryable failure, a structural one (missing parent, changed or deleted
            // branch) can never run again and leaves nothing for the user to resume, retry or
            // rebase, so its entry is dropped instead of blocking the queue. Any message queued
            // behind it then starts normally from the finally block below.
            else
            {
                runtime.State.FailUnrecoverable(Describe(error), FailureKind(error));
                // A failure writes no message, yet it is the outcome the user has to look at, so
                // it lifts the chat in the sidebar the same way a finished reply does.
                try { await chatMutations.MarkActivityCoreAsync(runtime.State.ProjectId, runtime.State.ChatId, CancellationToken.None); }
                catch (Exception markError) when (markError is IOException or UnauthorizedAccessException
                    or InvalidOperationException or Domain.Common.DomainException) { }
                // Started outside this lease: telling the lead submits into the main branch, which
                // takes the same chat lock.
                if (!_shutdown.IsCancellationRequested)
                {
                    var failure = Describe(error);
                    _ = Task.Run(() => ReportToLeadAsync(runtime,
                        $"Failed: {failure}\nThe branch is stopped until someone retries or discards its turn.",
                        _shutdown.Token));
                }
            }
            if (error is OperationCanceledException && runtime.ResumeRequested && !_shutdown.IsCancellationRequested) runtime.State.Resume();
            try { await SaveAsync(runtime, await chats.GetAsync(runtime.State.ProjectId, runtime.State.ChatId, CancellationToken.None), CancellationToken.None); }
            catch (Exception saveError) when (saveError is IOException or UnauthorizedAccessException)
            {
                runtime.State.Fail(saveError.Message, RunFailureKind.Storage);
                runtime.Snapshot = Snapshot(runtime.State, null, runtime.ActiveMessageId) with
                {
                    Context = runtime.Context,
                    TurnUsage = runtime.TurnUsage,
                    Kind = runtime.Snapshot.Kind,
                    InteractionSurface = runtime.Snapshot.InteractionSurface,
                    ShowInMainRuns = runtime.Snapshot.ShowInMainRuns
                };
                Publish();
            }
        }
        finally
        {
            using var lease = await synchronization.EnterAsync(runtime.State.ChatId, CancellationToken.None);
            // Successful turns complete their tracker when ChatAgent returns the change set for
            // the final message. An interrupted attempt keeps its baselines for resume/retry or
            // a new user message continuing the same unfinished round.
            if (runtime.State.Status != RunStatus.Completed)
                runtime.WorkspaceChanges = await workspace.SnapshotAsync(WorkspaceKey(runtime), CancellationToken.None);
            runtime.Cancellation?.Dispose();
            runtime.Cancellation = null;
            runtime.ActiveMessageId = null;
            runtime.Approval = null;
            runtime.PendingApproval = null;
            // A question outlives neither its run nor its turn: whoever was waiting on the answer
            // is already gone, so the card has to go with them rather than linger unanswerable.
            runtime.Prompt?.TrySetResult(new UserPromptResponse(runtime.PendingPrompt?.Id ?? Guid.Empty,
                UserPromptOutcome.Interrupted, []));
            runtime.Prompt = null;
            runtime.PendingPrompt = null;
            runtime.ActiveTools.Clear();
            runtime.Draft.Clear();
            runtime.DraftToolCall = null;
            runtime.Snapshot = runtime.Snapshot with
            {
                PendingApproval = null,
                PendingPrompt = null,
                ActiveTools = [],
                DraftContent = null,
                DraftToolCall = null,
                // This block patches the last published snapshot instead of rebuilding it, so
                // every field the worker just released has to be named here. Leaving this one out
                // left the run advertising an active command it had already finished with.
                ActiveMessageId = runtime.ActiveMessageId,
                WorkspaceChanges = runtime.State.Status == RunStatus.Completed ? null : runtime.WorkspaceChanges
            };
            Publish();
            runtime.Worker = null;
            StartWorker(runtime);
        }
    }

    private void StartTitleSkill(Guid projectId, Guid chatId)
    {
        lock (_titleTasksGate)
        {
            if (_updating || _shutdown.IsCancellationRequested || _titleTasks.ContainsKey(chatId)) return;
            var invocation = new SkillInvocation("chat-rename", projectId,
                System.Text.Json.JsonSerializer.SerializeToElement(new { chat_id = chatId, mode = "automatic" }), chatId);
            var task = Task.Run(() => skillRunner.RunAsync(invocation, _shutdown.Token));
            _titleTasks[chatId] = task;
            _ = task.ContinueWith(_ =>
            {
                lock (_titleTasksGate) _titleTasks.Remove(chatId);
            }, TaskScheduler.Default);
        }
    }

    public async Task<bool> DecideToolAsync(Guid projectId, Guid chatId, Guid branchId, ToolApprovalDecision decision, CancellationToken token)
    {
        Runtime runtime;
        ToolApproval approval;
        using (await synchronization.EnterAsync(chatId, token))
        {
            if (!_runtimes.TryGetValue(new RunKey(projectId, chatId, branchId), out runtime!)
                || runtime.PendingApproval?.Id != decision.ApprovalId
                || runtime.Cancellation?.IsCancellationRequested != false)
                return false;
            approval = runtime.PendingApproval;
        }

        switch (decision.Action)
        {
            case ToolApprovalAction.Allow:
            case ToolApprovalAction.Deny:
                break;
            case ToolApprovalAction.AllowForChat:
                var chat = await chats.GetAsync(projectId, chatId, token);
                var chatExisting = chat?.ToolPolicies?.SingleOrDefault(item => item.ServerId == approval.ServerId
                    && item.Name == approval.Name && item.SchemaHash == approval.SchemaHash);
                var chatPolicy = new ToolPolicySettings(approval.ServerId, approval.Name, approval.SchemaHash,
                    "Allow", chatExisting?.MaxCallsPerRun, chatExisting?.TimeoutSeconds);
                if (await chats.SetToolPolicyAsync(projectId, chatId, chatPolicy, token) is null) return false;
                break;
            case ToolApprovalAction.AllowForProject:
                var project = await projects.GetAsync(projectId, token);
                var projectExisting = project?.ToolPolicies.SingleOrDefault(item => item.ServerId == approval.ServerId
                    && item.Name == approval.Name && item.SchemaHash == approval.SchemaHash);
                var projectPolicy = new ToolPolicySettings(approval.ServerId, approval.Name, approval.SchemaHash,
                    "Allow", projectExisting?.MaxCallsPerRun, projectExisting?.TimeoutSeconds);
                if (await projects.SetToolPolicyAsync(projectId, projectPolicy, token) is null) return false;
                break;
            case ToolApprovalAction.AllowGlobally:
                var global = await settings.LoadAsync(token);
                var globalExisting = global.ToolPolicies.SingleOrDefault(item => item.ServerId == approval.ServerId
                    && item.Name == approval.Name && item.SchemaHash == approval.SchemaHash);
                await globalSettings.SetToolPolicyAsync(new McpToolPolicySettings(approval.ServerId,
                    approval.Name, approval.SchemaHash, "Allow", globalExisting?.MaxCallsPerRun ?? 65535,
                    globalExisting?.TimeoutSeconds ?? approval.TimeoutSeconds), token);
                break;
            default:
                return false;
        }

        using var lease = await synchronization.EnterAsync(chatId, token);
        if (runtime.PendingApproval?.Id != decision.ApprovalId) return false;
        return runtime.Approval?.TrySetResult(decision.Action) == true;
    }

    private async Task<ToolApprovalAction> ApproveAsync(Runtime runtime, AgentTool tool, string arguments, long timeout,
        ToolCallPosition position, CancellationToken token)
    {
        var (projectId, chatId, branchId) = (runtime.State.ProjectId, runtime.State.ChatId, runtime.State.BranchId);
        // The chat's mode answers first; a card is only for what it leaves to the person.
        var automatic = await autoApprover.DecideAsync(projectId, chatId, branchId, tool, arguments, token);
        if (automatic.Allowed) return ToolApprovalAction.Allow;
        if (runtime.State.Queue.FirstOrDefault(item => item.Id == runtime.ActiveMessageId)?.Interactive == false)
            return ToolApprovalAction.Deny;
        var mode = await autoApprover.ModeAsync(projectId, chatId, token);
        var completion = new TaskCompletionSource<ToolApprovalAction>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (await synchronization.EnterAsync(runtime.State.ChatId, token))
        {
            runtime.PendingApproval = new ToolApproval(ids.Create(), tool.ServerId, tool.OriginalName,
                tool.SchemaHash, arguments, timeout, position.Index, position.BatchSize, automatic.Reason);
            runtime.Approval = completion;
            runtime.State.Append("");
            await SaveAsync(runtime, await chats.GetAsync(runtime.State.ProjectId, runtime.State.ChatId, token), token);
        }
        try
        {
            while (true)
            {
                try { return await completion.Task.WaitAsync(PolicyRecheck, token); }
                catch (TimeoutException) { }
                // The card is not the only way to answer it. Someone who goes to settings and
                // grants the tool there has answered just as clearly, and expects the call they
                // were looking at to proceed — so the standing policy is re-read while waiting.
                var policy = await policies.ResolveAsync(runtime.State.ProjectId, runtime.State.ChatId,
                    tool.ServerId, tool.OriginalName, tool.SchemaHash, token);
                if (policy.Decision == "Allow") return ToolApprovalAction.Allow;
                if (policy.Decision == "Deny") return ToolApprovalAction.Deny;
                // Switching the chat's mode while the card waits answers it too: Full access lets the
                // call through, and "Approve for me" gets the one assessment it would have had.
                var current = await autoApprover.ModeAsync(projectId, chatId, token);
                if (current == mode) continue;
                mode = current;
                if (current != ToolApprovalMode.Ask
                    && (await autoApprover.DecideAsync(projectId, chatId, branchId, tool, arguments, token)).Allowed)
                    return ToolApprovalAction.Allow;
            }
        }
        finally
        {
            using var lease = await synchronization.EnterAsync(runtime.State.ChatId, CancellationToken.None);
            runtime.PendingApproval = null;
            runtime.Approval = null;
        }
    }

    /// <summary>
    /// Hands the person a question from a tool and waits. Everything about the wait matches a
    /// confirmation's — the card is snapshot state, the answer arrives through the dispatcher, the
    /// run holds still — because to the person the two are the same act: the work stopped for them.
    /// </summary>
    public async Task<UserPromptResponse> AskAsync(ToolRunContext run, UserPromptRequest request,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(request);
        var defaultSubmitAt = request.SubmitDefaults ? clock.UtcNow.Add(timeout) : (DateTimeOffset?)null;
        // Editing stops default submission but leaves a bounded period to finish answering.
        if (request.SubmitDefaults) timeout += TimeSpan.FromMinutes(2);
        var prompt = new UserPrompt(ids.Create(), request.Questions,
            timeout == Timeout.InfiniteTimeSpan ? 0 : (long)timeout.TotalSeconds,
            request.Presentation, request.SubmitDefaults,
            timeout == Timeout.InfiniteTimeSpan ? null : clock.UtcNow.Add(timeout), defaultSubmitAt);
        var unanswerable = new UserPromptResponse(prompt.Id, UserPromptOutcome.Interrupted, []);
        // Nobody to ask: a background run, or one already on its way out. Answered at once rather
        // than waited out, so a subtask reports what it could not decide instead of stalling on it.
        if (!run.Interactive && (request.Presentation != "overlay" || run.ChatId == Guid.Empty
                || !run.OverlayPromptsAllowed)) return unanswerable;

        var completion = new TaskCompletionSource<UserPromptResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        Runtime runtime;
        using (await synchronization.EnterAsync(run.ChatId, cancellationToken))
        {
            if (!_runtimes.TryGetValue(new RunKey(run.ProjectId, run.ChatId, run.BranchId), out runtime!)
                || runtime.Cancellation?.IsCancellationRequested != false)
                return unanswerable;
            runtime.PendingPrompt = prompt;
            runtime.Prompt = completion;
            runtime.State.Append("");
            await SaveAsync(runtime, await chats.GetAsync(run.ProjectId, run.ChatId, cancellationToken), cancellationToken);
        }

        try
        {
            return await completion.Task.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            // Walking away is not refusing. The model is told there is no answer and carries on
            // choosing for itself, which is what it would have done had it never asked.
            return new UserPromptResponse(prompt.Id, UserPromptOutcome.Expired, []);
        }
        finally
        {
            using var lease = await synchronization.EnterAsync(run.ChatId, CancellationToken.None);
            if (runtime.PendingPrompt?.Id == prompt.Id)
            {
                runtime.PendingPrompt = null;
                runtime.Prompt = null;
                runtime.State.Append("");
                await SaveAsync(runtime, await chats.GetAsync(run.ProjectId, run.ChatId, CancellationToken.None),
                    CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Delivers an answer to the question a run is waiting on. Stale answers are refused rather
    /// than applied: a card left open in a second window names a prompt that is no longer current,
    /// and answering the question that replaced it is not what that click meant.
    /// </summary>
    public async Task<bool> AnswerPromptAsync(Guid projectId, Guid chatId, Guid branchId,
        UserPromptResponse response, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(response);
        using var lease = await synchronization.EnterAsync(chatId, token);
        if (!_runtimes.TryGetValue(new RunKey(projectId, chatId, branchId), out var runtime)
            || runtime.PendingPrompt?.Id != response.PromptId)
            return false;
        return runtime.Prompt?.TrySetResult(response) == true;
    }

    /// <summary>
    /// Records a call starting, progressing, or ending. Only the start and the end touch storage:
    /// a progress report changes nothing durable, so it updates the snapshot and publishes, and is
    /// throttled the same way streamed text is — a chatty server must not turn into disk traffic.
    /// </summary>
    private async Task ReportToolActivityAsync(Runtime runtime, ToolActivity? activity, CancellationToken token)
    {
        using var lease = await synchronization.EnterAsync(runtime.State.ChatId, token);
        if (activity is null)
        {
            runtime.ActiveTools.Clear();
            // The file tool has returned, so its effects can be shown while the model continues.
            runtime.WorkspaceChanges = await workspace.SnapshotAsync(WorkspaceKey(runtime), token);
            runtime.State.Append("");
            await SaveAsync(runtime, await chats.GetAsync(runtime.State.ProjectId, runtime.State.ChatId, token), token);
            return;
        }

        var started = runtime.ActiveTools.TryGetValue(activity.CallId, out var existing);
        var invocation = new ActiveToolInvocation(activity.CallId, activity.Name, activity.Arguments,
            started ? existing!.StartedAt : clock.UtcNow, activity.Progress, activity.Total, activity.Message);
        runtime.ActiveTools[activity.CallId] = invocation;

        if (!started)
        {
            runtime.LastProgressPublished = clock.UtcNow;
            runtime.State.Append("");
            await SaveAsync(runtime, await chats.GetAsync(runtime.State.ProjectId, runtime.State.ChatId, token), token);
            return;
        }

        if (clock.UtcNow - runtime.LastProgressPublished < ProgressPublishInterval) return;
        runtime.LastProgressPublished = clock.UtcNow;
        runtime.Snapshot = runtime.Snapshot with { ActiveTools = runtime.ActiveTools.Values.ToArray() };
        Publish();
    }

    private static readonly TimeSpan ProgressPublishInterval = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Mirrors the model step in flight into the snapshot. Nothing here touches storage: a draft
    /// is either replaced by what the step becomes or discarded with it. A reset is not published
    /// on its own — the save that follows it (the preamble landing, the answer arriving) carries
    /// it, so the text changes hands in one publication.
    /// </summary>
    private async Task ReportDraftAsync(Runtime runtime, string? chunk, CancellationToken token)
    {
        using var lease = await synchronization.EnterAsync(runtime.State.ChatId, token);
        if (chunk is null)
        {
            runtime.Draft.Clear();
            runtime.DraftToolCall = null;
            return;
        }

        runtime.Draft.Append(chunk);
        if (clock.UtcNow - runtime.LastDraftPublished < ProgressPublishInterval) return;
        runtime.LastDraftPublished = clock.UtcNow;
        runtime.Snapshot = runtime.Snapshot with { DraftContent = runtime.DraftContent };
        Publish();
    }

    /// <summary>
    /// Publishes at once, unlike the prose: the start of a call is one event, and the transcript
    /// uses it to stop presenting the text before it as a possible answer.
    /// </summary>
    private async Task ReportDraftToolCallAsync(Runtime runtime, string name, CancellationToken token)
    {
        using var lease = await synchronization.EnterAsync(runtime.State.ChatId, token);
        if (runtime.DraftToolCall == name) return;
        runtime.DraftToolCall = name;
        runtime.LastDraftPublished = clock.UtcNow;
        runtime.Snapshot = runtime.Snapshot with { DraftContent = runtime.DraftContent, DraftToolCall = name };
        Publish();
    }

    private async Task ReportContextAsync(Runtime runtime, ContextUsage usage, CancellationToken token)
    {
        using var lease = await synchronization.EnterAsync(runtime.State.ChatId, token);
        runtime.Context = usage;
        runtime.Snapshot = runtime.Snapshot with { Context = usage };
        Publish();
    }

    private async Task ReportTokenUsageAsync(Runtime runtime, TokenUsageRecord record, CancellationToken token)
    {
        using var lease = await synchronization.EnterAsync(runtime.State.ChatId, token);
        if (record.TurnId is not { } turnId) return;
        runtime.TurnRecords.Add(record);
        runtime.TurnUsage = new TurnTokenUsage(turnId, runtime.State.BranchId, runtime.TurnRecords[0].At,
            usageAggregator.Total(runtime.TurnRecords),
            usageAggregator.Group(runtime.TurnRecords, item => item.Purpose.ToString()),
            usageAggregator.AnswerModels(runtime.TurnRecords));
        runtime.Snapshot = runtime.Snapshot with { TurnUsage = runtime.TurnUsage };
        Publish();
    }

    private async Task ReportTransportActivityAsync(Runtime runtime, ChatTransportWait? wait, CancellationToken token)
    {
        using var lease = await synchronization.EnterAsync(runtime.State.ChatId, token);
        runtime.Wait = wait is null ? null : new ChatRunWait(ChatRunWaitKind.RateLimit, wait.RetryAt, wait.Attempt);
        runtime.State.Append("");
        await SaveAsync(runtime, null, token);
    }

    private static WorkspaceRunKey WorkspaceKey(Runtime runtime) =>
        new(runtime.State.ProjectId, runtime.State.ChatId, runtime.State.BranchId);

    private async Task PersistToolMessageAsync(Runtime runtime, ChatCompletionMessage message, CancellationToken token)
    {
        using var lease = await synchronization.EnterAsync(runtime.State.ChatId, token);
        var chat = await chats.GetAsync(runtime.State.ProjectId, runtime.State.ChatId, token)
            ?? throw new InvalidOperationException("Chat not found.");
        var baseRevision = chat.Revision;
        var id = ids.Create();
        chat = await chatMutations.AppendMessageCoreAsync(chat.ProjectId, chat.Id,
            new AppendChatMessageRequest(id, runtime.ToolHead, message.Role, message.Content, chat.Revision,
                BranchId: runtime.State.BranchId, ToolCalls: message.ToolCalls, ToolCallId: message.ToolCallId),
            RetainedMessageIds(chat.Id), token)
            ?? throw new InvalidOperationException("Tool history conflict.");
        TrackMessage(runtime, baseRevision, chat, id);
        runtime.ToolHead = id;
        if (runtime.State.Status == RunStatus.Generating)
        {
            runtime.State.Start();
        }
        await SaveAsync(runtime, chat, token);
    }

    // The open chat takes the run's messages from its snapshots. The question and the answer are
    // carried as well as the tool steps: a message missing from the tail made the client fetch the
    // whole transcript at the start and at the end of every turn, and redraw all of it.
    // A tool's output goes without its body, as in the transcript: the page loads it when the step is
    // opened. A file read sent in full was hundreds of kilobytes for the page to parse on the thread
    // that also handles typing, for a step that is folded away.
    private static void TrackMessage(Runtime runtime, long baseRevision, ChatDetails chat, Guid messageId)
    {
        var message = chat.Messages.Single(item => item.Id == messageId);
        if (message.Role == "Tool" && message.Content.Length > 0)
            message = message with { Content = string.Empty, ContentOmitted = true };
        runtime.TrackMessage(baseRevision, chat.Revision, message, RecentMessageCapacity);
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
            if (message.StartsTurn) break;
            cursor = message.ParentId;
        }
        return userId;
    }

    private static Guid? ResolveParent(ChatDetails chat, Guid branchId, QueuedRunMessage queued)
    {
        var branch = chat.Branches?.SingleOrDefault(item => item.Id == branchId);
        if (queued.ReplaceSourceId is { } sourceId)
        {
            if (branch is null) throw new RunDispatchException(RunFailureKind.BranchDeleted, "The target branch was deleted.");
            if (queued.ExpectedBranchRevision is { } revision && branch.Revision != revision)
                throw new RunDispatchException(RunFailureKind.BranchChanged, "The branch changed after the replacement was queued.");
            var source = chat.Messages.SingleOrDefault(message => message.Id == sourceId)
                ?? throw new RunDispatchException(RunFailureKind.ParentMissing, "The replacement source no longer exists.");
            if (!IsAncestor(chat, branch.HeadMessageId, sourceId))
                throw new RunDispatchException(RunFailureKind.BranchChanged, "The replacement source is no longer on the target branch.");
            return source.ParentId;
        }

        return queued.ParentMode switch
        {
            Domain.Runs.MessageParentMode.Root => null,
            Domain.Runs.MessageParentMode.BranchHead => ResolveBranchHead(branch),
            Domain.Runs.MessageParentMode.Explicit => ResolveExplicitParent(chat, queued),
            _ => throw new RunDispatchException(RunFailureKind.ParentMissing, "The queued parent mode is invalid.")
        };
    }

    private static Guid? ResolveBranchHead(ChatBranchView? branch)
    {
        if (branch is null) throw new RunDispatchException(RunFailureKind.BranchDeleted, "The target branch was deleted.");
        return branch.HeadMessageId;
    }

    private static Guid ResolveExplicitParent(ChatDetails chat, QueuedRunMessage queued)
    {
        var parent = queued.ParentMessageId
            ?? throw new RunDispatchException(RunFailureKind.ParentMissing, "The queued message has no explicit parent.");
        if (chat.Messages.All(message => message.Id != parent))
            throw new RunDispatchException(RunFailureKind.ParentMissing, "The queued message parent no longer exists.");
        if (queued.ParentBranchId is { } parentBranchId)
        {
            var parentBranch = chat.Branches?.SingleOrDefault(branch => branch.Id == parentBranchId)
                ?? throw new RunDispatchException(RunFailureKind.BranchDeleted, "The parent branch was deleted.");
            if (!IsAncestor(chat, parentBranch.HeadMessageId, parent))
                throw new RunDispatchException(RunFailureKind.BranchChanged, "The queued parent no longer belongs to its branch.");
        }
        return parent;
    }

    private static bool IsAncestor(ChatDetails chat, Guid? headId, Guid messageId)
    {
        var byId = chat.Messages.ToDictionary(message => message.Id);
        var visited = new HashSet<Guid>();
        var cursor = headId;
        while (cursor is { } id && visited.Add(id) && byId.TryGetValue(id, out var message))
        {
            if (id == messageId) return true;
            cursor = message.ParentId;
        }
        return false;
    }

    /// <summary>
    /// What a failed run says about itself. A message the application itself wrote — a missing
    /// branch, a refused endpoint — stands on its own. A fault from anywhere else does not: "Object
    /// reference not set to an instance of an object" names neither the fault nor its origin, so the
    /// type and the first frame of this assembly's own stack are added. Without them such a failure
    /// can only be investigated by guessing, which is how it went the last time one happened.
    /// </summary>
    private static string Describe(Exception error) => error switch
    {
        RunDispatchException or InvalidOperationException or ArgumentException or HttpRequestException
            or Domain.Common.DomainException => error.Message,
        _ => $"{error.GetType().Name}: {error.Message}{Origin(error)}",
    };

    private static string Origin(Exception error)
    {
        var frame = (error.StackTrace ?? string.Empty)
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("at AI.", StringComparison.Ordinal));
        return frame is null ? string.Empty : $" ({frame[3..]})";
    }

    private static RunFailureKind FailureKind(Exception error) => error switch
    {
        RunDispatchException dispatch => dispatch.FailureKind,
        ContextWindowExceededException => RunFailureKind.ContextWindow,
        IOException or UnauthorizedAccessException => RunFailureKind.Storage,
        HttpRequestException => RunFailureKind.Transient,
        _ => RunFailureKind.Transient
    };

    private static Guid ReplyId(Guid messageId) => DerivedId($"assistant:{messageId:N}");

    /// <summary>
    /// Id of the truncated answer an interrupted attempt leaves behind. Deliberately different
    /// from <see cref="ReplyId"/>: the "this command already has its reply, treat it as done"
    /// check keys on the full answer, and a partial one must never satisfy it. Deterministic, so
    /// committing the same interrupted attempt twice cannot produce two messages.
    /// </summary>
    private static Guid PartialReplyId(Guid messageId) => DerivedId($"assistant-partial:{messageId:N}");

    private static Guid DerivedId(string seed)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>
    /// Persists whatever the model had produced before the attempt was cut short, as an
    /// incomplete assistant message. Without it the text simply disappeared when a run was
    /// stopped or failed, and the user message was left in the transcript with nothing under it.
    /// Never allowed to throw: it runs from the worker's own failure path, where the error that
    /// got us here is the one worth reporting.
    /// </summary>
    private async Task CommitPartialAnswerAsync(Runtime runtime)
    {
        // Whitespace is not an answer: a message made of it is refused by the domain, so there is
        // nothing to keep and nothing to report.
        if (runtime.ActiveMessageId is not { } active) return;
        var partialContent = runtime.State.StreamingContent;
        if (string.IsNullOrWhiteSpace(partialContent)) return;
        if (_maintenance.ContainsKey(runtime.State.ChatId) || _deletingProjects.ContainsKey(runtime.State.ProjectId)) return;
        try
        {
            var chat = await chats.GetAsync(runtime.State.ProjectId, runtime.State.ChatId, CancellationToken.None);
            if (chat is null || chat.Messages.All(message => message.Id != active)) return;
            var partialId = PartialReplyId(active);
            if (chat.Messages.Any(message => message.Id == partialId))
            {
                runtime.State.ClearStreaming();
                return;
            }
            var appended = await chatMutations.AppendMessageCoreAsync(chat.ProjectId, chat.Id,
                new AppendChatMessageRequest(partialId, runtime.ToolHead ?? active, "Assistant",
                    partialContent, chat.Revision, IsIncomplete: true,
                    BranchId: runtime.State.BranchId), RetainedMessageIds(chat.Id), CancellationToken.None);
            if (appended is not null)
            {
                runtime.State.ClearStreaming();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or InvalidOperationException or ArgumentException or Domain.Common.DomainException) { }
    }

    private async Task SaveAsync(Runtime runtime, ChatDetails? chat, CancellationToken token)
    {
        await repository.SaveAsync(runtime.State, token);
        var previous = runtime.Snapshot;
        runtime.Snapshot = Snapshot(runtime.State, chat, runtime.ActiveMessageId) with
        {
            ChatRevision = chat?.Revision ?? previous.ChatRevision,
            HeadMessageId = chat?.Branches?.SingleOrDefault(branch => branch.Id == runtime.State.BranchId)?.HeadMessageId
                ?? previous.HeadMessageId,
            BranchRevision = chat?.Branches?.SingleOrDefault(branch => branch.Id == runtime.State.BranchId)?.Revision
                ?? previous.BranchRevision,
            PendingApproval = runtime.PendingApproval,
            PendingPrompt = runtime.PendingPrompt,
            ActiveTools = runtime.ActiveTools.Values.ToArray(),
            WorkspaceChanges = runtime.WorkspaceChanges,
            Wait = runtime.Wait,
            MessageDelta = runtime.MessageDelta,
            DraftContent = runtime.DraftContent,
            DraftToolCall = runtime.DraftToolCall,
            Context = runtime.Context,
            TurnUsage = runtime.TurnUsage,
            Kind = chat?.Kind ?? previous.Kind,
            InteractionSurface = chat?.InteractionSurface ?? previous.InteractionSurface,
            ShowInMainRuns = chat?.ShowInMainRuns ?? previous.ShowInMainRuns
        };
        if (chat is not null)
            foreach (var other in _runtimes.Values.Where(item => item.State.ChatId == chat.Id && item != runtime))
                other.Snapshot = other.Snapshot with
                {
                    ChatRevision = chat.Revision,
                    HeadMessageId = chat.Branches?.SingleOrDefault(branch => branch.Id == other.State.BranchId)?.HeadMessageId,
                    BranchRevision = chat.Branches?.SingleOrDefault(branch => branch.Id == other.State.BranchId)?.Revision ?? 0
                };
        Publish();
    }

    public async Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        foreach (var chatId in _runtimes.Keys.Where(key => key.ProjectId == projectId).Select(key => key.ChatId).Distinct())
            await RemoveAsync(projectId, chatId, null, cancellationToken);
        await repository.DeleteProjectAsync(projectId, cancellationToken);
        await resources.DeleteProjectAsync(projectId, cancellationToken);
        await reviews.DeleteProjectAsync(projectId, cancellationToken);
        await memory.DeleteProjectAsync(projectId, cancellationToken);
        await skillCatalog.DeleteProjectAsync(projectId, cancellationToken);
        await projectInstructions.DeleteProjectAsync(projectId, cancellationToken);
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
            var result = await chatMutations.DeleteAsync(projectId, chatId, revision, cancellationToken);
            if (result.IsDeleted)
            {
                await RemoveAsync(projectId, chatId, null, CancellationToken.None);
                await reviews.DeleteChatAsync(projectId, chatId, CancellationToken.None);
            }
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
            if (chat is null) return new ChatBranchDeleteResult(false, 0, null, null);
            if (chat.Revision != revision) return new ChatBranchDeleteResult(false, chat.Revision, null, null);
            await PauseBranchWorkerAsync(projectId, chatId, branchId, cancellationToken);
            var retainedMessages = RetainedMessageIds(chatId, excludedBranchId: branchId);
            var result = await chatMutations.DeleteBranchAsync(projectId, chatId, branchId, revision, retainedMessages, cancellationToken);
            if (!result.IsDeleted)
            {
                return result;
            }

            var latest = await chats.GetAsync(projectId, chatId, CancellationToken.None);
            var retained = branchIds.Collect(latest!);
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

    /// <summary>
    /// Cancels whatever the branch is generating and waits for its worker to unwind, so a caller
    /// about to rewrite the queue sees a settled run: truncated answer committed, no active
    /// command, no worker about to write over the change. Does nothing when the operation has
    /// already been applied, so a resubmitted request cannot stop a run a second time.
    /// </summary>
    private async Task InterruptBranchAsync(Guid projectId, Guid chatId, Guid branchId, Guid operationId, CancellationToken token)
    {
        Task worker;
        using (await synchronization.EnterAsync(chatId, token))
        {
            if (!_runtimes.TryGetValue(new RunKey(projectId, chatId, branchId), out var runtime)) return;
            if (operationId != Guid.Empty && runtime.State.Operations.Contains(operationId)) return;
            runtime.ResumeRequested = false;
            if (runtime.Cancellation is { } cancellation) await cancellation.CancelAsync();
            if (runtime.Worker is not null) runtime.State.Pause();
            worker = runtime.Worker ?? Task.CompletedTask;
        }
        await worker.WaitAsync(token);
    }

    private async Task InterruptMessageAsync(Guid projectId, Guid chatId, Guid branchId, Guid messageId, Guid operationId, CancellationToken token)
    {
        Task worker;
        using (await synchronization.EnterAsync(chatId, token))
        {
            if (!_runtimes.TryGetValue(new RunKey(projectId, chatId, branchId), out var runtime)
                || runtime.ActiveMessageId != messageId
                || operationId != Guid.Empty && runtime.State.Operations.Contains(operationId)) return;
            runtime.ResumeRequested = false;
            if (runtime.Cancellation is { } cancellation) await cancellation.CancelAsync();
            if (runtime.Worker is not null) runtime.State.Pause();
            worker = runtime.Worker ?? Task.CompletedTask;
        }
        await worker.WaitAsync(token);
    }

    private async Task PauseBranchWorkerAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken token)
    {
        Task worker;
        using (await synchronization.EnterAsync(chatId, token))
        {
            if (!_runtimes.TryGetValue(new RunKey(projectId, chatId, branchId), out var runtime)) return;
            runtime.ResumeRequested = false;
            if (runtime.Cancellation is { } cancellation) await cancellation.CancelAsync();
            if (runtime.Worker is not null) runtime.State.Pause();
            worker = runtime.Worker ?? Task.CompletedTask;
        }
        await worker.WaitAsync(token);
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

    // The whole queue is published, stage and all, including the command the run is working
    // from. Hiding that entry while it generated is what made the queue read differently from one
    // moment to the next: it vanished on start and came back on failure, and the first waiting
    // message inherited its "Running" badge. The client decides what a stage means; the snapshot
    // just says what is true.
    private static ChatRunSnapshot Snapshot(ChatRunState state, ChatDetails? chat, Guid? activeMessageId = null) => new(state.ProjectId, state.ChatId, state.BranchId,
        (ChatRunStatus)state.Status, state.StreamingContent,
        state.Queue.Select(item => new QueuedChatMessage(item.Id, item.Content, item.CreatedAt,
            ParentMode(item.ParentMode), item.ParentMessageId, Stage(item.Stage),
            ResourceReferences.ToContract(item.Resources), item.IsAside, ToContract(item.Sender), item.JoinsTurn)).ToArray(),
        state.HasUnreadResponse, state.Error, state.Revision, chat?.Revision ?? 0,
        chat?.Branches?.SingleOrDefault(branch => branch.Id == state.BranchId)?.HeadMessageId,
        FailureCode: FailureCode(state.FailureKind), CanRetry: state.CanRetry,
        BranchRevision: chat?.Branches?.SingleOrDefault(branch => branch.Id == state.BranchId)?.Revision ?? 0,
        RecoveryActions: RecoveryActions(state), ActiveMessageId: activeMessageId,
        Kind: chat?.Kind ?? "conversation", InteractionSurface: chat?.InteractionSurface ?? "chat",
        ShowInMainRuns: chat?.ShowInMainRuns ?? true);

    private static QueuedMessageStage Stage(QueuedRunStage stage) => stage switch
    {
        QueuedRunStage.UserCommitted => QueuedMessageStage.UserCommitted,
        _ => QueuedMessageStage.Prepared
    };

    private static RunRecoveryAction[] RecoveryActions(ChatRunState state)
    {
        if (state.Status is RunStatus.Paused or RunStatus.Interrupted) return [RunRecoveryAction.Resume];
        if (state.Status != RunStatus.Failed) return [];
        var result = new List<RunRecoveryAction>();
        if (state.CanRetry) result.Add(RunRecoveryAction.Retry);
        if (state.Queue.Count > 0)
        {
            if (state.FailureKind is RunFailureKind.ParentMissing or RunFailureKind.BranchChanged)
                result.Add(RunRecoveryAction.Rebase);
            result.Add(RunRecoveryAction.Skip);
        }
        return result.ToArray();
    }

    private static RunFailureCode FailureCode(RunFailureKind kind) => kind switch
    {
        RunFailureKind.None => RunFailureCode.None,
        RunFailureKind.Transient => RunFailureCode.Transient,
        RunFailureKind.BranchChanged => RunFailureCode.BranchChanged,
        RunFailureKind.BranchDeleted => RunFailureCode.BranchDeleted,
        RunFailureKind.ParentMissing => RunFailureCode.ParentMissing,
        RunFailureKind.Storage => RunFailureCode.Storage,
        RunFailureKind.ContextWindow => RunFailureCode.ContextWindow,
        _ => RunFailureCode.Transient
    };

    private static Contracts.Runs.MessageParentMode ParentMode(Domain.Runs.MessageParentMode mode) => mode switch
    {
        Domain.Runs.MessageParentMode.Root => Contracts.Runs.MessageParentMode.Root,
        Domain.Runs.MessageParentMode.Explicit => Contracts.Runs.MessageParentMode.Explicit,
        Domain.Runs.MessageParentMode.BranchHead => Contracts.Runs.MessageParentMode.BranchHead,
        _ => Contracts.Runs.MessageParentMode.BranchHead
    };

    private HashSet<Guid> RetainedMessageIds(Guid chatId, Guid? committingMessageId = null, Guid? excludedBranchId = null) =>
        _runtimes.Values
            .Where(item => item.State.ChatId == chatId && item.State.BranchId != excludedBranchId)
            .SelectMany(item => item.State.Queue.SelectMany(message =>
                {
                    var committed = message.Stage == QueuedRunStage.UserCommitted || message.Id == committingMessageId;
                    return new Guid?[]
                    {
                        message.ParentMessageId,
                        committed ? message.Id : message.ReplaceSourceId
                    };
                })
                .Append(item.ToolHead))
            .OfType<Guid>().ToHashSet();

    private static ChatRunState Clone(ChatRunState state) => ChatRunState.Restore(state.ProjectId, state.ChatId, state.BranchId,
        state.Status, state.StreamingContent, state.Error, state.FailureKind, state.HasUnreadResponse, state.Revision,
        state.Queue.ToArray(), state.Operations.ToArray());

    private sealed class Runtime(ChatRunState state)
    {
        private readonly Queue<ChatMessageAppend> _recentMessages = new();
        public ChatRunState State { get; set; } = state;
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public ChatRunSnapshot Snapshot { get; set; } = ChatRunDispatcher.Snapshot(state, null);
        public Guid? ToolHead { get; set; }
        public ToolApproval? PendingApproval { get; set; }
        public TaskCompletionSource<ToolApprovalAction>? Approval { get; set; }
        public UserPrompt? PendingPrompt { get; set; }
        public TaskCompletionSource<UserPromptResponse>? Prompt { get; set; }
        /// <summary>Live tool calls, keyed by call id. A list, so parallel calls need no contract change.</summary>
        public Dictionary<string, ActiveToolInvocation> ActiveTools { get; } = [];

        /// <summary>When the last progress-only update was published, for throttling.</summary>
        public DateTimeOffset LastProgressPublished { get; set; }

        /// <summary>The current turn's live change set; the final copy is stored on its reply.</summary>
        public WorkspaceChangeSet? WorkspaceChanges { get; set; }
        public Task? Worker { get; set; }
        public CancellationTokenSource? Cancellation { get; set; }
        public Guid? ActiveMessageId { get; set; }
        public bool ResumeRequested { get; set; }
        public DateTimeOffset LastPublished { get; set; }
        public ChatRunWait? Wait { get; set; }
        /// <summary>The last measured context of this branch; kept in memory only.</summary>
        public ContextUsage? Context { get; set; }
        /// <summary>The requests of the current or last turn, as they were measured; kept in memory only.</summary>
        public List<TokenUsageRecord> TurnRecords { get; } = [];
        public TurnTokenUsage? TurnUsage { get; set; }

        /// <summary>The prose of the model step in flight; never persisted.</summary>
        public System.Text.StringBuilder Draft { get; } = new();
        public DateTimeOffset LastDraftPublished { get; set; }
        /// <summary>The tool the step in flight has started to call; cleared with the draft.</summary>
        public string? DraftToolCall { get; set; }
        public string? DraftContent => Draft.Length == 0 ? null : Draft.ToString();

        public ChatMessageDelta? MessageDelta => _recentMessages.Count == 0
            ? null
            : new ChatMessageDelta(_recentMessages.ToArray());

        public void TrackMessage(long baseRevision, long revision, ChatMessageView message, int capacity)
        {
            if (_recentMessages.TryPeek(out _) && _recentMessages.Last().Revision != baseRevision)
                _recentMessages.Clear();
            _recentMessages.Enqueue(new ChatMessageAppend(baseRevision, revision, message));
            while (_recentMessages.Count > capacity) _recentMessages.Dequeue();
        }
    }

    private readonly record struct RunKey(Guid ProjectId, Guid ChatId, Guid BranchId);
}
