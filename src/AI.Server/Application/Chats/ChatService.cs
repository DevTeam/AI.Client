// ReSharper disable UseCollectionExpression
namespace AI.Application.Chats;

using Projects;
using AI.Contracts.Chats;
using AI.Contracts.Projects;
using AI.Contracts.Tools;
using AI.Contracts.Workspace;
using AI.Domain.Chats;
using AI.Domain.Projects;
using AI.Application.Resources;

public sealed class ChatService(IChatRepository repository, IIdGenerator idGenerator, IClock clock, IChatSynchronization synchronization,
    IPinOrderKeys pinOrderKeys, Func<IChatKindPolicyRegistry> kindPolicies) : IChatService, IChatMutations
{
    private IChatKindPolicyRegistry KindPolicies => kindPolicies();

    public async Task<IReadOnlyList<ChatSummary>> ListAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var summaries = (await repository.ListSummariesAsync(new ProjectId(projectId), cancellationToken))
            .Where(item => KindPolicies.TryResolve(item.Kind)?.Behavior.ShowInChatList == true).ToArray();
        return OrderPinned(summaries.Where(item => item.IsPinned))
            .Concat(summaries.Where(item => !item.IsPinned).OrderByDescending(item => item.LastActivityAt))
            .Select(ToSummary)
            .ToArray();
    }

    public async Task<bool> ExistsAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken) =>
        (await repository.ListSummariesAsync(new ProjectId(projectId), cancellationToken))
        .Any(item => item.Id.Value == chatId);

    /// <summary>
    /// Pinned chats keep the order the user gave them; activity never reorders them. Chats pinned
    /// before manual ordering existed have no key yet and come first, in the order they were
    /// pinned, which is also where a newly pinned chat would have gone.
    /// </summary>
    private static IEnumerable<StoredChatSummary> OrderPinned(IEnumerable<StoredChatSummary> pinned) =>
        pinned
            .OrderBy(item => item.PinOrder is not null)
            .ThenBy(item => item.PinOrder, StringComparer.Ordinal)
            .ThenBy(item => item.PinnedAt)
            .ThenBy(item => item.Id.Value);

    private static ChatSummary ToSummary(StoredChatSummary item) => new(
        item.Id.Value,
        item.ProjectId.Value,
        item.Title,
        item.UpdatedAt,
        item.Revision,
        item.LastActivityAt,
        item.IsPinned,
        item.PinnedAt,
        item.BranchCount,
        item.IsEmpty, item.ArchivedAt, item.ArchiveOperationId,
        item.Kind == default ? ChatKind.Conversation.Value : item.Kind.Value);

    public async Task<ChatDetails?> GetAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        return stored is null ? null : ToDetails(stored.Chat, stored.Revision);
    }

    public async Task<ChatDetails?> GetTranscriptAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        return stored is null ? null : ToTranscript(stored.Chat, stored.Revision);
    }

    public async Task<ChatMessageView?> GetReviewSourceAsync(Guid projectId, Guid chatId, Guid messageId,
        CancellationToken cancellationToken)
    {
        var found = await repository.GetMessageAsync(new ProjectId(projectId), new ChatId(chatId),
            new ChatMessageId(messageId), cancellationToken);
        return found?.Message is { } message ? ToView(message) : null;
    }

    public async Task<ChatTurnActivity?> GetTurnActivityAsync(
        Guid projectId,
        Guid chatId,
        Guid turnId,
        Guid branchLeafId,
        CancellationToken cancellationToken)
    {
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null) return null;

        IReadOnlyList<ChatMessage> branch;
        try
        {
            branch = stored.Chat.GetBranch(new ChatMessageId(branchLeafId));
        }
        catch (AI.Domain.Common.DomainException)
        {
            return null;
        }

        var start = -1;
        for (var index = 0; index < branch.Count; index++)
        {
            if (branch[index].Id.Value == turnId && branch[index].StartsTurn)
            {
                start = index;
                break;
            }
        }
        if (start < 0) return null;

        var end = start + 1;
        while (end < branch.Count && !branch[end].StartsTurn) end++;
        // A completed turn's last plain assistant message is already present in the compact
        // transcript as the final answer. Everything before it is expandable activity.
        if (end > start + 1 && IsPlainAssistant(branch[end - 1])) end--;

        // The expanded feed needs the small checkpoint result to decide whether compaction
        // actually applied. Other tool output remains lazy, even when the turn is expanded.
        var compactCallIds = branch.Skip(start + 1).Take(end - start - 1)
            .SelectMany(message => message.ToolCalls ?? [])
            .Where(call => ToolRef.Parse(call.Name) is { IsApp: true, Name: "context_compact" })
            .Select(call => call.Id)
            .ToHashSet(StringComparer.Ordinal);
        var messages = branch
            .Skip(start + 1)
            .Take(end - start - 1)
            .Select(message => ToView(message, omitToolResultContent:
                message.Role != ChatMessageRole.Tool
                || message.ToolCallId is not { } callId
                || !compactCallIds.Contains(callId)
                || message.Content.Length > 4096))
            .ToArray();
        return new ChatTurnActivity(stored.Revision, turnId, messages);
    }

    public async Task<ChatMessageContent?> GetMessageContentAsync(
        Guid projectId,
        Guid chatId,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        var message = stored?.Chat.Messages.SingleOrDefault(item => item.Id.Value == messageId);
        return message is null || message.Role != ChatMessageRole.Tool
            ? null
            : new ChatMessageContent(stored!.Revision, messageId, message.Content);
    }

    public async Task<bool> RemoveReviewReferencesAsync(Guid projectId, Guid chatId,
        Guid reviewId, CancellationToken cancellationToken)
    {
        using var lease = await synchronization.EnterAsync(chatId, cancellationToken);
        var project = new ProjectId(projectId);
        var id = new ChatId(chatId);
        var mayContainReference = await repository.MayContainReviewReferenceAsync(project, id, reviewId, cancellationToken);
        if (mayContainReference is null) return false;
        if (mayContainReference == false) return true;
        var stored = await repository.GetAsync(project, id, cancellationToken);
        if (stored is null) return false;
        if (!stored.Chat.RemoveReviewReferences(reviewId, clock.UtcNow)) return true;
        var result = await repository.SaveAsync(stored.Chat, stored.Revision, cancellationToken);
        if (!result.IsSaved) throw new InvalidOperationException("Could not remove review links from the chat.");
        return true;
    }

    public async Task<ChatDetails> CreateAsync(Guid projectId, CreateChatRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.UtcNow;
        var kind = new ChatKind(request.Kind);
        var policy = KindPolicies.Resolve(kind);
        policy.ValidateState(request.KindState, request.KindStateVersion);
        var chat = new ChatThread(
            new ChatId(idGenerator.Create()),
            new ProjectId(projectId),
            request.Title,
            now,
            request.ConnectionId is { } endpointId ? new ConnectionId(endpointId) : null,
            request.AutoTitlePending, kind, request.KindState, request.KindStateVersion);
        if (request.ApprovalMode != ToolApprovalMode.Ask) chat.SetApprovalMode(ToDomain(request.ApprovalMode), now);
        var result = await repository.SaveAsync(chat, 0, cancellationToken);
        return await policy.InitializeAsync(ToDetails(chat, result.Revision), this, cancellationToken);
    }

    public async Task<ChatDetails?> AppendMessageAsync(Guid projectId, Guid chatId, AppendChatMessageRequest request, CancellationToken cancellationToken)
    {
        using var lease = await synchronization.EnterAsync(chatId, cancellationToken);
        return await AppendMessageCoreAsync(projectId, chatId, request, new HashSet<Guid>(), cancellationToken);
    }

    public async Task<ChatDetails?> AppendMessageCoreAsync(
        Guid projectId,
        Guid chatId,
        AppendChatMessageRequest request,
        IReadOnlySet<Guid> retainedMessageIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null)
        {
            return null;
        }

        if (!Enum.TryParse<ChatMessageRole>(request.Role, true, out var role))
        {
            throw new ArgumentException($"Unsupported chat role '{request.Role}'.", nameof(request));
        }

        var now = clock.UtcNow;
        var message = new ChatMessage(
            new ChatMessageId(request.Id ?? idGenerator.Create()),
            request.ParentId is { } parentId ? new ChatMessageId(parentId) : null,
            role,
            request.Content,
            now,
            request.IsIncomplete,
            request.ToolCalls?.Select(call => new ChatToolCall(call.Id, call.Name, call.Arguments)).ToArray(),
            request.ToolCallId,
            ToDomain(request.WorkspaceChanges), ResourceReferences.ToDomain(request.Resources),
            delivery: (ChatMessageDelivery)request.Delivery,
            sender: request.Sender is { } sender ? new ChatMessageSender(sender.ChatId, sender.BranchId, sender.Intent) : null);
        if (role == ChatMessageRole.User) stored.Chat.SetArchived(false, Guid.Empty, now);
        if (request.ReplaceSourceId is { } replaceId)
        {
            stored.Chat.ReplaceInBranch(request.BranchId ?? throw new ArgumentException("A replacement branch is required."),
                new ChatMessageId(replaceId), message, now);
            // Replace is destructive by contract: once the replacement and the new branch head
            // are ready, the abandoned tail is no longer a user-visible version. Prune before the
            // repository save so either the whole replacement is persisted or the original chat
            // remains intact. Queued work may still own otherwise unreachable anchors, supplied by
            // the dispatcher as retained roots.
            stored.Chat.PruneUnreachableMessages(retainedMessageIds.Select(id => new ChatMessageId(id)));
        }
        else
            stored.Chat.AddMessage(message, now, request.BranchId, request.ParentBranchId, request.BranchTitle,
                request.BranchMember is { } member ? new ChatBranchMember(member.Name, member.Role, string.Empty) : null);
        var result = await repository.SaveAsync(stored.Chat, request.Revision, cancellationToken);
        return result.IsSaved ? ToDetails(stored.Chat, result.Revision) : null;
    }

    /// <summary>
    /// Moves a branch head back to <paramref name="headMessageId"/> and drops everything the
    /// abandoned attempt left behind it. Callers hold the chat lease already.
    /// </summary>
    public async Task<ChatDetails?> RewindBranchCoreAsync(Guid projectId, Guid chatId, Guid branchId,
        Guid headMessageId, IReadOnlySet<Guid> retainedMessageIds, CancellationToken cancellationToken)
    {
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null) return null;
        if (!stored.Chat.RewindBranchTo(branchId, new ChatMessageId(headMessageId), clock.UtcNow))
            return ToDetails(stored.Chat, stored.Revision);
        stored.Chat.PruneUnreachableMessages(retainedMessageIds.Select(id => new ChatMessageId(id)));
        var result = await repository.SaveAsync(stored.Chat, stored.Revision, cancellationToken);
        return result.IsSaved ? ToDetails(stored.Chat, result.Revision) : null;
    }

    public async Task<ChatDetails?> PruneMessagesCoreAsync(Guid projectId, Guid chatId,
        IReadOnlySet<Guid> retainedMessageIds, CancellationToken cancellationToken)
    {
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null) return null;
        if (!stored.Chat.PruneUnreachableMessages(retainedMessageIds.Select(id => new ChatMessageId(id))))
            return ToDetails(stored.Chat, stored.Revision);
        var result = await repository.SaveAsync(stored.Chat, stored.Revision, cancellationToken);
        return result.IsSaved ? ToDetails(stored.Chat, result.Revision) : null;
    }

    public async Task<ChatDetails?> LoadForRunRecoveryAsync(Guid projectId, Guid chatId,
        IReadOnlySet<Guid> retainedMessageIds, CancellationToken cancellationToken)
    {
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null || KindPolicies.TryResolve(stored.Chat.Kind) is null) return null;
        if (!stored.Chat.PruneUnreachableMessages(retainedMessageIds.Select(id => new ChatMessageId(id))))
            return ToDetails(stored.Chat, stored.Revision);

        var result = await repository.SaveAsync(stored.Chat, stored.Revision, cancellationToken);
        return result.IsSaved ? ToDetails(stored.Chat, result.Revision)
            : await GetAsync(projectId, chatId, cancellationToken);
    }

    public async Task<ChatDetails?> UpdateEndpointAsync(
        Guid projectId,
        Guid chatId,
        UpdateChatEndpointRequest request,
        CancellationToken cancellationToken)
    {
        using var lease = await synchronization.EnterAsync(chatId, cancellationToken);
        ArgumentNullException.ThrowIfNull(request);
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null)
        {
            return null;
        }

        stored.Chat.SetConnection(
            request.ConnectionId is { } endpointId ? new ConnectionId(endpointId) : null,
            clock.UtcNow);
        var result = await repository.SaveAsync(stored.Chat, request.Revision, cancellationToken);
        return result.IsSaved ? ToTranscript(stored.Chat, result.Revision) : null;
    }

    public async Task<ChatDetails?> UpdateApprovalModeAsync(
        Guid projectId,
        Guid chatId,
        UpdateChatApprovalModeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var lease = await synchronization.EnterAsync(chatId, cancellationToken);
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null) return null;
        stored.Chat.SetApprovalMode(ToDomain(request.Mode), clock.UtcNow);
        // Saved against the revision just read, under the chat's lease: a run appending messages
        // must not turn the person's choice into a conflict they have to repeat.
        var result = await repository.SaveAsync(stored.Chat, stored.Revision, cancellationToken);
        return result.IsSaved ? ToTranscript(stored.Chat, result.Revision) : null;
    }

    private static ChatApprovalMode ToDomain(ToolApprovalMode mode) => mode switch
    {
        ToolApprovalMode.Ask => ChatApprovalMode.Ask,
        ToolApprovalMode.Auto => ChatApprovalMode.Auto,
        ToolApprovalMode.FullAccess => ChatApprovalMode.FullAccess,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown approval mode.")
    };

    private static ToolApprovalMode ToContract(ChatApprovalMode mode) => mode switch
    {
        ChatApprovalMode.Auto => ToolApprovalMode.Auto,
        ChatApprovalMode.FullAccess => ToolApprovalMode.FullAccess,
        _ => ToolApprovalMode.Ask
    };

    public Task<ChatDetails?> UpdateBranchSettingsAsync(Guid projectId, Guid chatId, Guid branchId,
        UpdateBranchSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Settings);
        return ChangeBranchSettingsAsync(projectId, chatId, branchId, _ => request.Settings, cancellationToken);
    }

    public async Task<ChatDetails?> ChangeBranchSettingsAsync(Guid projectId, Guid chatId, Guid branchId,
        Func<BranchSettings, BranchSettings> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        using var lease = await synchronization.EnterAsync(chatId, cancellationToken);
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null) return null;
        var current = stored.Chat.Branches.FirstOrDefault(branch => branch.Id == branchId)?.Settings;
        var settings = change(ToContract(current) ?? new BranchSettings());
        var policies = settings.ToolPolicies is { Count: > 0 } items ? items.Select(ToPolicy).ToArray() : null;
        // A branch that overrides nothing stores no settings at all, so it reads as plainly inheriting.
        stored.Chat.SetBranchSettings(branchId, settings.ConnectionId is null && settings.ApprovalMode is null && policies is null
            ? null
            : new ChatBranchSettings(settings.ConnectionId is { } connection ? new ConnectionId(connection) : null,
                settings.ApprovalMode is { } mode ? ToDomain(mode) : null, policies), clock.UtcNow);
        var result = await repository.SaveAsync(stored.Chat, stored.Revision, cancellationToken);
        return result.IsSaved ? ToTranscript(stored.Chat, result.Revision) : null;
    }

    public async Task<ChatSummary?> PinAsync(
        Guid projectId,
        Guid chatId,
        PinChatRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // The key is computed before this chat's lease is taken: placing it in front of a chat
        // pinned before manual ordering existed first gives those chats keys, one lease at a time.
        var order = request.IsPinned
            ? await PlacePinnedAsync(new ProjectId(projectId), new ChatId(chatId), request.BeforeChatId, cancellationToken)
            : null;
        using var lease = await synchronization.EnterAsync(chatId, cancellationToken);
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null) return null;

        var now = clock.UtcNow;
        if (order is not null) stored.Chat.Pin(order, now);
        else stored.Chat.Unpin(now);

        var result = await repository.SaveAsync(stored.Chat, request.Revision, cancellationToken);
        if (!result.IsSaved) return null;
        return new ChatSummary(
            stored.Chat.Id.Value,
            stored.Chat.ProjectId.Value,
            stored.Chat.Title,
            stored.Chat.UpdatedAt,
            result.Revision,
            stored.Chat.LastActivityAt,
            stored.Chat.IsPinned,
            stored.Chat.PinnedAt,
            stored.Chat.BranchCount,
            stored.Chat.Messages.Count == 0, stored.Chat.ArchivedAt, stored.Chat.ArchiveOperationId, stored.Chat.Kind.Value);
    }

    /// <summary>Returns the key that puts <paramref name="chatId"/> in front of <paramref name="beforeChatId"/>
    /// among the project's pinned chats, or last when that chat is not pinned or not given.</summary>
    private async Task<string> PlacePinnedAsync(
        ProjectId projectId, ChatId chatId, Guid? beforeChatId, CancellationToken cancellationToken)
    {
        var pinned = await ListPinnedExceptAsync(projectId, chatId, cancellationToken);
        var index = beforeChatId is { } before ? pinned.FindIndex(item => item.Id.Value == before) : -1;
        if (index >= 0 && pinned[index].PinOrder is null)
        {
            await AssignPinOrderAsync(projectId, pinned, cancellationToken);
            pinned = await ListPinnedExceptAsync(projectId, chatId, cancellationToken);
            index = pinned.FindIndex(item => item.Id.Value == beforeChatId);
        }

        return index < 0
            ? pinOrderKeys.Between(pinned.LastOrDefault()?.PinOrder, null)
            : pinOrderKeys.Between(index > 0 ? pinned[index - 1].PinOrder : null, pinned[index].PinOrder);
    }

    private async Task<List<StoredChatSummary>> ListPinnedExceptAsync(
        ProjectId projectId, ChatId chatId, CancellationToken cancellationToken) =>
        OrderPinned((await repository.ListSummariesAsync(projectId, cancellationToken))
            .Where(item => item.IsPinned && item.Id != chatId)).ToList();

    /// <summary>
    /// Gives keys to the pinned chats that predate manual ordering, keeping the order they are
    /// shown in: they all sit in front of the keyed ones, so each takes a key below the first key.
    /// </summary>
    private async Task AssignPinOrderAsync(
        ProjectId projectId, IReadOnlyList<StoredChatSummary> pinned, CancellationToken cancellationToken)
    {
        var upper = pinned.FirstOrDefault(item => item.PinOrder is not null)?.PinOrder;
        string? previous = null;
        foreach (var item in pinned.Where(item => item.PinOrder is null))
        {
            var order = pinOrderKeys.Between(previous, upper);
            using var lease = await synchronization.EnterAsync(item.Id.Value, cancellationToken);
            var stored = await repository.GetAsync(projectId, item.Id, cancellationToken);
            // Unpinned or already keyed by a concurrent move in the meantime: leave it be.
            if (stored is null || !stored.Chat.IsPinned || stored.Chat.PinOrder is not null) continue;
            stored.Chat.Pin(order, clock.UtcNow);
            if ((await repository.SaveAsync(stored.Chat, stored.Revision, cancellationToken)).IsSaved) previous = order;
        }
    }

    public async Task<ChatDetails?> MarkActivityCoreAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null) return null;
        stored.Chat.MarkActivity(clock.UtcNow);
        var result = await repository.SaveAsync(stored.Chat, stored.Revision, cancellationToken);
        return result.IsSaved ? ToDetails(stored.Chat, result.Revision) : null;
    }

    public async Task<ChatDetails?> RenameAsync(
        Guid projectId,
        Guid chatId,
        RenameChatRequest request,
        CancellationToken cancellationToken)
    {
        using var lease = await synchronization.EnterAsync(chatId, cancellationToken);
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null) return null;
        stored.Chat.Rename(request.Title, clock.UtcNow);
        // As with a branch title: a chat title conflicts with nothing written meanwhile, and the
        // revision a sidebar row carries is stale after the chat's next message, so renaming
        // against it failed whenever the chat had moved on. The chat lock makes this save safe.
        var result = await repository.SaveAsync(stored.Chat, stored.Revision, cancellationToken);
        return result.IsSaved ? ToTranscript(stored.Chat, result.Revision) : null;
    }

    public async Task<ChatDetails?> ApplyAutomaticTitleAsync(
        Guid projectId, Guid chatId, string title, CancellationToken cancellationToken)
    {
        using var lease = await synchronization.EnterAsync(chatId, cancellationToken);
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null || !stored.Chat.ApplyAutomaticTitle(title, clock.UtcNow)) return null;
        var result = await repository.SaveAsync(stored.Chat, stored.Revision, cancellationToken);
        return result.IsSaved ? ToDetails(stored.Chat, result.Revision) : null;
    }

    public async Task<ChatDeleteResult> DeleteAsync(
        Guid projectId,
        Guid chatId,
        long revision,
        CancellationToken cancellationToken)
    {
        using var lease = await synchronization.EnterAsync(chatId, cancellationToken);
        return await repository.DeleteAsync(new ProjectId(projectId), new ChatId(chatId), revision, cancellationToken);
    }

    public async Task<ChatDetails?> RenameBranchAsync(Guid projectId, Guid chatId, Guid branchId, RenameChatBranchRequest request, CancellationToken cancellationToken)
    {
        using var lease = await synchronization.EnterAsync(chatId, cancellationToken);
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null) return null;
        stored.Chat.RenameBranch(new ChatMessageId(branchId), request.Title, clock.UtcNow);
        // A title conflicts with nothing written meanwhile, and while branches run the chat's
        // revision moves with every message, so a rename against the revision the caller read
        // could keep failing for as long as a team works. The chat lock makes saving against the
        // stored revision safe; the request's revision is only what the caller last saw.
        var result = await repository.SaveAsync(stored.Chat, stored.Revision, cancellationToken);
        return result.IsSaved ? ToTranscript(stored.Chat, result.Revision) : null;
    }

    public async Task<ChatDetails?> SetToolPolicyAsync(Guid projectId, Guid chatId,
        ToolPolicySettings policy, CancellationToken cancellationToken)
    {
        using var lease = await synchronization.EnterAsync(chatId, cancellationToken);
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null) return null;
        stored.Chat.SetToolPolicy(ToPolicy(policy), clock.UtcNow);
        var result = await repository.SaveAsync(stored.Chat, stored.Revision, cancellationToken);
        return result.IsSaved ? ToTranscript(stored.Chat, result.Revision) : null;
    }

    public async Task<ChatKindChange?> ChangeKindAsync(Guid projectId, Guid chatId,
        Func<ChatKindState, ChatKindState?> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        using var lease = await synchronization.EnterAsync(chatId, cancellationToken);
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null) return null;
        var current = new ChatKindState(stored.Chat.Kind.Value, stored.Chat.KindState, stored.Chat.KindStateVersion);
        if (change(current) is not { } next) return new ChatKindChange(current, stored.Revision, false);
        var kind = new ChatKind(next.Kind);
        // The storage route is chosen by the kind's persistence; moving a chat between routes would
        // leave it behind in the old one.
        if (KindPolicies.Resolve(kind).Behavior.Persistence != KindPolicies.Resolve(stored.Chat.Kind).Behavior.Persistence)
            throw new InvalidOperationException($"A '{stored.Chat.Kind}' chat cannot become '{kind}'.");
        KindPolicies.Resolve(kind).ValidateState(next.State, next.Version);
        stored.Chat.SetKind(kind, next.State, next.Version, clock.UtcNow);
        var result = await repository.SaveAsync(stored.Chat, stored.Revision, cancellationToken);
        if (!result.IsSaved) throw new InvalidOperationException("Could not change the chat kind.");
        return new ChatKindChange(new ChatKindState(kind.Value, stored.Chat.KindState, next.Version), result.Revision, true);
    }

    public async Task<ChatDetails?> RemoveToolPolicyAsync(Guid projectId, Guid chatId,
        Guid serverId, string name, string schemaHash, CancellationToken cancellationToken)
    {
        using var lease = await synchronization.EnterAsync(chatId, cancellationToken);
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null) return null;
        stored.Chat.RemoveToolPolicy(new ToolIdentity(new McpServerId(serverId), name, schemaHash), clock.UtcNow);
        var result = await repository.SaveAsync(stored.Chat, stored.Revision, cancellationToken);
        return result.IsSaved ? ToTranscript(stored.Chat, result.Revision) : null;
    }

    public async Task<ChatBranchDeleteResult> DeleteBranchAsync(Guid projectId, Guid chatId, Guid branchId, long revision,
        IReadOnlySet<Guid> retainedMessageIds, CancellationToken cancellationToken)
    {
        using var lease = await synchronization.EnterAsync(chatId, cancellationToken);
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null) return new ChatBranchDeleteResult(false, 0, null, null);
        var parent = stored.Chat.DeleteBranch(branchId, clock.UtcNow);
        stored.Chat.PruneUnreachableMessages(retainedMessageIds.Select(id => new ChatMessageId(id)));
        var result = await repository.SaveAsync(stored.Chat, revision, cancellationToken);
        return new ChatBranchDeleteResult(result.IsSaved, result.Revision, parent.ParentBranchId,
            parent.ParentHeadMessageId?.Value);
    }

    private ChatDetails ToDetails(ChatThread chat, long revision) => new(
        chat.Id.Value,
        chat.ProjectId.Value,
        chat.Title,
        chat.CreatedAt,
        chat.UpdatedAt,
        revision,
        chat.ConnectionId?.Value,
        chat.Messages.OrderBy(item => item.CreatedAt).Select(message => ToView(message)).ToArray(),
        chat.Branches.Select(branch => new ChatBranchView(branch.Id, branch.HeadMessageId?.Value, branch.Title,
            branch.ParentBranchId, branch.RootMessageId?.Value, branch.Revision, ToContract(branch.Member),
            ToContract(branch.Settings))).ToArray(),
        chat.ToolPolicies.Select(policy => new ToolPolicySettings(policy.Tool.ServerId.Value, policy.Tool.Name,
            policy.Tool.SchemaHash, policy.Decision.ToString(), policy.MaxCallsPerRun,
            policy.Timeout is { } timeout ? checked((long)timeout.TotalSeconds) : null)).ToArray(),
        chat.AutoTitlePending, chat.ArchivedAt, chat.ArchiveOperationId, ToContract(chat.ApprovalMode),
        chat.Kind.Value, chat.KindState, KindPolicies.TryResolve(chat.Kind)?.Behavior.InteractionSurface ?? "unsupported",
        KindPolicies.TryResolve(chat.Kind)?.Behavior.AllowChatNavigation ?? false,
        KindPolicies.TryResolve(chat.Kind)?.Behavior.ShowInMainRuns ?? false, chat.KindStateVersion);

    // Also the answer to a change of the chat's settings (connection, approval mode, titles, tool
    // policies): the page may keep it as the open chat, and the full chat carries every tool
    // output — megabytes for a long run — for the page to parse and re-read on each render.
    private ChatDetails ToTranscript(ChatThread chat, long revision)
    {
        var messages = chat.Messages.OrderBy(item => item.CreatedAt).ToArray();
        var branchHeads = chat.Branches
            .Select(branch => branch.HeadMessageId)
            .OfType<ChatMessageId>()
            .ToHashSet();
        var followedByUser = messages
            .Where(message => message.StartsTurn && message.ParentId is not null)
            .Select(message => message.ParentId!.Value)
            .ToHashSet();
        var unansweredNotes = GetUnansweredTurnNotes(messages, branchHeads);

        var projected = messages.Select(message =>
        {
            var keepContent = message.Role == ChatMessageRole.User
                || unansweredNotes.Contains(message.Id)
                || IsPlainAssistant(message)
                && (branchHeads.Contains(message.Id) || followedByUser.Contains(message.Id));
            // The feed renders each completed turn's file-change receipt from the transcript.
            // Keep it even when the message body is folded; the live run stops carrying it on completion.
            return ToView(message, omitContent: !keepContent, omitToolArguments: true);
        }).ToArray();

        return new ChatDetails(
            chat.Id.Value,
            chat.ProjectId.Value,
            chat.Title,
            chat.CreatedAt,
            chat.UpdatedAt,
            revision,
            chat.ConnectionId?.Value,
            projected,
            chat.Branches.Select(branch => new ChatBranchView(branch.Id, branch.HeadMessageId?.Value, branch.Title,
                branch.ParentBranchId, branch.RootMessageId?.Value, branch.Revision, ToContract(branch.Member),
                ToContract(branch.Settings))).ToArray(),
            chat.ToolPolicies.Select(policy => new ToolPolicySettings(policy.Tool.ServerId.Value, policy.Tool.Name,
                policy.Tool.SchemaHash, policy.Decision.ToString(), policy.MaxCallsPerRun,
                policy.Timeout is { } timeout ? checked((long)timeout.TotalSeconds) : null)).ToArray(),
            chat.AutoTitlePending, chat.ArchivedAt, chat.ArchiveOperationId, ToContract(chat.ApprovalMode),
            chat.Kind.Value, chat.KindState, KindPolicies.TryResolve(chat.Kind)?.Behavior.InteractionSurface ?? "unsupported",
            KindPolicies.TryResolve(chat.Kind)?.Behavior.AllowChatNavigation ?? false,
            KindPolicies.TryResolve(chat.Kind)?.Behavior.ShowInMainRuns ?? false, chat.KindStateVersion);
    }

    // The model's notes in a branch's last turn while it has no answer yet. A running turn shows
    // its latest note under the turn row; folded away, a chat opened mid-run had nothing to show
    // there until the run wrote its next step. Tool arguments and results stay omitted.
    private static HashSet<ChatMessageId> GetUnansweredTurnNotes(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlySet<ChatMessageId> branchHeads)
    {
        var byId = messages.ToDictionary(message => message.Id);
        var notes = new HashSet<ChatMessageId>();
        foreach (var head in branchHeads)
        {
            var current = byId.GetValueOrDefault(head);
            if (current is null || IsPlainAssistant(current)) continue;
            while (current is not null && !current.StartsTurn)
            {
                if (current.Role == ChatMessageRole.Assistant) notes.Add(current.Id);
                current = current.ParentId is { } parent ? byId.GetValueOrDefault(parent) : null;
            }
        }
        return notes;
    }

    private static bool IsPlainAssistant(ChatMessage message) =>
        message.Role == ChatMessageRole.Assistant && message.ToolCalls is not { Count: > 0 };

    private static ChatMessageView ToView(
        ChatMessage message,
        bool omitContent = false,
        bool omitToolArguments = false,
        bool omitToolResultContent = false)
    {
        var contentOmitted = (omitContent || omitToolResultContent && message.Role == ChatMessageRole.Tool)
            && message.Content.Length > 0;
        return new ChatMessageView(
            message.Id.Value,
            message.ParentId?.Value,
            message.Role.ToString(),
            contentOmitted ? string.Empty : message.Content,
            message.CreatedAt,
            message.IsIncomplete,
            message.ToolCalls?.Select(call => new Contracts.Chat.ChatToolCall(
                call.Id,
                call.Name,
                omitToolArguments ? string.Empty : call.Arguments)).ToArray(),
            message.ToolCallId,
            ToContract(message.WorkspaceChanges),
            contentOmitted,
            ResourceReferences.ToContract(message.Resources),
            message.Role == ChatMessageRole.Tool ? ToolResultErrorFlag(message.Content) : null,
            (MessageDelivery)message.Delivery,
            message.Sender is { } sender ? new MessageSender(sender.ChatId, sender.BranchId, sender.Intent) : null);
    }

    private static TeamMember? ToContract(ChatBranchMember? member) =>
        member is null ? null : new TeamMember(member.Name, member.Role, member.Color);

    private static BranchSettings? ToContract(ChatBranchSettings? settings) => settings is null ? null : new(
        settings.ConnectionId?.Value,
        settings.ApprovalMode is { } mode ? ToContract(mode) : null,
        settings.ToolPolicies?.Select(policy => new ToolPolicySettings(policy.Tool.ServerId.Value,
            policy.Tool.Name, policy.Tool.SchemaHash, policy.Decision.ToString(), policy.MaxCallsPerRun,
            policy.Timeout is { } timeout ? checked((long)timeout.TotalSeconds) : null)).ToArray());

    private static bool? ToolResultErrorFlag(string content)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(content);
            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && document.RootElement.TryGetProperty("isError", out var flag)
                && flag.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False
                    ? flag.GetBoolean() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static ChatWorkspaceChangeSet? ToDomain(WorkspaceChangeSet? changes) => changes is null
        ? null
        : new ChatWorkspaceChangeSet(
            changes.Files.Select(file => new ChatFileChange(
                file.Path,
                (ChatFileChangeKind)file.Kind,
                file.Additions,
                file.Deletions,
                file.PreviousPath,
                file.Diff,
                file.IsBinary,
                (ChatFileChangeConfidence)file.Confidence)).ToArray(),
            changes.Additions,
            changes.Deletions,
            changes.UndoId);

    private static WorkspaceChangeSet? ToContract(ChatWorkspaceChangeSet? changes) => changes is null
        ? null
        : new WorkspaceChangeSet(
            changes.Files.Select(file => new FileChange(
                file.Path,
                (FileChangeKind)file.Kind,
                file.Additions,
                file.Deletions,
                file.PreviousPath,
                file.Diff,
                file.IsBinary,
                (FileChangeConfidence)file.Confidence)).ToArray(),
            changes.Additions,
            changes.Deletions,
            changes.UndoId);

    private static ToolPolicy ToPolicy(ToolPolicySettings policy) => new(
        new ToolIdentity(new McpServerId(policy.ServerId), policy.Name, policy.SchemaHash),
        Enum.TryParse<ToolPolicyDecision>(policy.Decision, true, out var decision)
            ? decision : throw new ArgumentException($"Unsupported tool policy '{policy.Decision}'."),
        policy.MaxCallsPerRun,
        policy.TimeoutSeconds is { } timeout ? TimeSpan.FromSeconds(timeout) : null);
}
