using AI.Client.Application.Projects;
using AI.Client.Contracts.Chats;
using AI.Client.Domain.Chats;
using AI.Client.Domain.Projects;

namespace AI.Client.Application.Chats;

public sealed class ChatService(IChatRepository repository, IProjectIdGenerator idGenerator, IClock clock) : IChatService
{
    public async Task<IReadOnlyList<ChatSummary>> ListAsync(Guid projectId, CancellationToken cancellationToken) =>
        (await repository.ListAsync(new ProjectId(projectId), cancellationToken))
        .OrderByDescending(item => item.Chat.UpdatedAt)
        .Select(item => new ChatSummary(
            item.Chat.Id.Value,
            item.Chat.ProjectId.Value,
            item.Chat.Title,
            item.Chat.UpdatedAt,
            item.Revision))
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
            new ChatId(idGenerator.Create().Value),
            new ProjectId(projectId),
            request.Title,
            now,
            request.EndpointProfileId is { } endpointId ? new EndpointProfileId(endpointId) : null);
        var result = await repository.SaveAsync(chat, 0, cancellationToken);
        return ToDetails(chat, result.Revision);
    }

    public async Task<ChatDetails?> AppendMessageAsync(
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
        stored.Chat.AddMessage(new ChatMessage(
            new ChatMessageId(request.Id ?? idGenerator.Create().Value),
            request.ParentId is { } parentId ? new ChatMessageId(parentId) : null,
            role,
            request.Content,
            now,
            request.IsIncomplete), now);
        var result = await repository.SaveAsync(stored.Chat, request.Revision, cancellationToken);
        return result.IsSaved ? ToDetails(stored.Chat, result.Revision) : null;
    }

    public async Task<ChatDetails?> UpdateEndpointAsync(
        Guid projectId,
        Guid chatId,
        UpdateChatEndpointRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null)
        {
            return null;
        }

        stored.Chat.SetEndpointProfile(
            request.EndpointProfileId is { } endpointId ? new EndpointProfileId(endpointId) : null,
            clock.UtcNow);
        var result = await repository.SaveAsync(stored.Chat, request.Revision, cancellationToken);
        return result.IsSaved ? ToDetails(stored.Chat, result.Revision) : null;
    }

    public async Task<ChatDetails?> RenameAsync(
        Guid projectId,
        Guid chatId,
        RenameChatRequest request,
        CancellationToken cancellationToken)
    {
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null) return null;
        stored.Chat.Rename(request.Title, clock.UtcNow);
        var result = await repository.SaveAsync(stored.Chat, request.Revision, cancellationToken);
        return result.IsSaved ? ToDetails(stored.Chat, result.Revision) : null;
    }

    public Task<ChatDeleteResult> DeleteAsync(
        Guid projectId,
        Guid chatId,
        long revision,
        CancellationToken cancellationToken) =>
        repository.DeleteAsync(new ProjectId(projectId), new ChatId(chatId), revision, cancellationToken);

    public async Task<ChatDetails?> RenameBranchAsync(Guid projectId, Guid chatId, Guid branchId, RenameChatBranchRequest request, CancellationToken cancellationToken)
    {
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null) return null;
        stored.Chat.RenameBranch(new ChatMessageId(branchId), request.Title, clock.UtcNow);
        var result = await repository.SaveAsync(stored.Chat, request.Revision, cancellationToken);
        return result.IsSaved ? ToDetails(stored.Chat, result.Revision) : null;
    }

    public async Task<ChatBranchDeleteResult> DeleteBranchAsync(Guid projectId, Guid chatId, Guid branchId, long revision, CancellationToken cancellationToken)
    {
        var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken);
        if (stored is null) return new(false, 0, null);
        var parentId = stored.Chat.DeleteBranch(new ChatMessageId(branchId), clock.UtcNow);
        var result = await repository.SaveAsync(stored.Chat, revision, cancellationToken);
        return new(result.IsSaved, result.Revision, parentId?.Value);
    }

    private static ChatDetails ToDetails(ChatThread chat, long revision) => new(
        chat.Id.Value,
        chat.ProjectId.Value,
        chat.Title,
        chat.CreatedAt,
        chat.UpdatedAt,
        revision,
        chat.EndpointProfileId?.Value,
        chat.Messages
            .OrderBy(item => item.CreatedAt)
            .Select(item => new ChatMessageView(
                item.Id.Value,
                item.ParentId?.Value,
                item.Role.ToString(),
                item.Content,
                item.CreatedAt,
                item.IsIncomplete))
            .ToArray(),
        chat.BranchTitles.ToDictionary(item => item.Key.Value, item => item.Value));
}
