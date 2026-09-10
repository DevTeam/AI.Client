// ReSharper disable UseCollectionExpression
namespace AI.Client.Application.Chats;

using Projects;
using AI.Client.Contracts.Chats;
using AI.Client.Contracts.Projects;
using AI.Client.Domain.Chats;
using AI.Client.Domain.Projects;

public sealed class ChatService(IChatRepository repository, IIdGenerator idGenerator, IClock clock, ChatSynchronization synchronization) : IChatService
{
    public async Task<IReadOnlyList<ChatSummary>> ListAsync(Guid projectId, CancellationToken cancellationToken) =>
        (await repository.ListAsync(new ProjectId(projectId), cancellationToken))
        .OrderByDescending(item => item.Chat.IsPinned)
        .ThenByDescending(item => item.Chat.LastActivityAt)
        .Select(item => new ChatSummary(
            item.Chat.Id.Value,
            item.Chat.ProjectId.Value,
            item.Chat.Title,
            item.Chat.UpdatedAt,
            item.Revision,
            item.Chat.LastActivityAt,
            item.Chat.IsPinned,
            item.Chat.PinnedAt))
        .ToArray();

    public async Task<ChatDetails?> GetAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        return stored is null ? null : ToDetails(stored.Chat, stored.Revision);
    }

    public async Task<ChatDetails> CreateAsync(Guid projectId, CreateChatRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.UtcNow;
        var chat = new ChatThread(
            new ChatId(idGenerator.Create()),
            new ProjectId(projectId),
            request.Title,
            now,
            request.ConnectionId is { } endpointId ? new ConnectionId(endpointId) : null);
        var result = await repository.SaveAsync(chat, 0, cancellationToken);
        return ToDetails(chat, result.Revision);
    }

    public async Task<ChatDetails?> AppendMessageAsync(Guid projectId, Guid chatId, AppendChatMessageRequest request, CancellationToken cancellationToken)
    {
        using var lease = await synchronization.EnterAsync(chatId, cancellationToken);
        return await AppendMessageCoreAsync(projectId, chatId, request, cancellationToken);
    }

    internal async Task<ChatDetails?> AppendMessageCoreAsync(
        Guid projectId,
        Guid chatId,
        AppendChatMessageRequest request,
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
            request.ToolCallId);
        if (request.ReplaceSourceId is { } replaceId)
            stored.Chat.ReplaceInBranch(request.BranchId ?? throw new ArgumentException("A replacement branch is required."),
                new ChatMessageId(replaceId), message, now);
        else
            stored.Chat.AddMessage(message, now, request.BranchId, request.ParentBranchId);
        var result = await repository.SaveAsync(stored.Chat, request.Revision, cancellationToken);
        return result.IsSaved ? ToDetails(stored.Chat, result.Revision) : null;
    }

    internal async Task<ChatDetails?> PruneMessagesCoreAsync(Guid projectId, Guid chatId,
        IReadOnlySet<Guid> retainedMessageIds, CancellationToken cancellationToken)
    {
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null) return null;
        if (!stored.Chat.PruneUnreachableMessages(retainedMessageIds.Select(id => new ChatMessageId(id))))
            return ToDetails(stored.Chat, stored.Revision);
        var result = await repository.SaveAsync(stored.Chat, stored.Revision, cancellationToken);
        return result.IsSaved ? ToDetails(stored.Chat, result.Revision) : null;
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
        return result.IsSaved ? ToDetails(stored.Chat, result.Revision) : null;
    }

    public async Task<ChatSummary?> PinAsync(
        Guid projectId,
        Guid chatId,
        PinChatRequest request,
        CancellationToken cancellationToken)
    {
        using var lease = await synchronization.EnterAsync(chatId, cancellationToken);
        ArgumentNullException.ThrowIfNull(request);
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null) return null;

        var now = clock.UtcNow;
        if (request.IsPinned) stored.Chat.Pin(now);
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
            stored.Chat.PinnedAt);
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
        var result = await repository.SaveAsync(stored.Chat, request.Revision, cancellationToken);
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
        var result = await repository.SaveAsync(stored.Chat, request.Revision, cancellationToken);
        return result.IsSaved ? ToDetails(stored.Chat, result.Revision) : null;
    }

    public async Task<ChatDetails?> SetToolPolicyAsync(Guid projectId, Guid chatId,
        ToolPolicySettings policy, CancellationToken cancellationToken)
    {
        using var lease = await synchronization.EnterAsync(chatId, cancellationToken);
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null) return null;
        stored.Chat.SetToolPolicy(ToPolicy(policy), clock.UtcNow);
        var result = await repository.SaveAsync(stored.Chat, stored.Revision, cancellationToken);
        return result.IsSaved ? ToDetails(stored.Chat, result.Revision) : null;
    }

    public async Task<ChatDetails?> RemoveToolPolicyAsync(Guid projectId, Guid chatId,
        Guid serverId, string name, string schemaHash, CancellationToken cancellationToken)
    {
        using var lease = await synchronization.EnterAsync(chatId, cancellationToken);
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null) return null;
        stored.Chat.RemoveToolPolicy(new ToolIdentity(new McpServerId(serverId), name, schemaHash), clock.UtcNow);
        var result = await repository.SaveAsync(stored.Chat, stored.Revision, cancellationToken);
        return result.IsSaved ? ToDetails(stored.Chat, result.Revision) : null;
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

    private static ChatDetails ToDetails(ChatThread chat, long revision) => new(
        chat.Id.Value,
        chat.ProjectId.Value,
        chat.Title,
        chat.CreatedAt,
        chat.UpdatedAt,
        revision,
        chat.ConnectionId?.Value,
        chat.Messages
            .OrderBy(item => item.CreatedAt)
            .Select(item => new ChatMessageView(
                item.Id.Value,
                item.ParentId?.Value,
                item.Role.ToString(),
                item.Content,
                item.CreatedAt,
                item.IsIncomplete,
                item.ToolCalls?.Select(call => new Contracts.Chat.ChatToolCall(call.Id, call.Name, call.Arguments)).ToArray(),
                item.ToolCallId))
            .ToArray(),
        chat.Branches.Select(branch => new ChatBranchView(branch.Id, branch.HeadMessageId?.Value, branch.Title,
            branch.ParentBranchId, branch.RootMessageId?.Value, branch.Revision)).ToArray(),
        chat.ToolPolicies.Select(policy => new ToolPolicySettings(policy.Tool.ServerId.Value, policy.Tool.Name,
            policy.Tool.SchemaHash, policy.Decision.ToString(), policy.MaxCallsPerRun,
            checked((long)policy.Timeout.TotalSeconds))).ToArray());

    private static ToolPolicy ToPolicy(ToolPolicySettings policy) => new(
        new ToolIdentity(new McpServerId(policy.ServerId), policy.Name, policy.SchemaHash),
        Enum.TryParse<ToolPolicyDecision>(policy.Decision, true, out var decision)
            ? decision : throw new ArgumentException($"Unsupported tool policy '{policy.Decision}'."),
        policy.MaxCallsPerRun,
        TimeSpan.FromSeconds(policy.TimeoutSeconds));
}
