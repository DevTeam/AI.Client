namespace AI.Infrastructure.Storage;

using System.Collections.Concurrent;
using AI.Application.Chats;
using AI.Application.Runs;
using AI.Domain.Chats;
using AI.Domain.Projects;
using AI.Domain.Runs;

/// <summary>Uses the chat's storage lifetime for its run state as well, resolving kind policies after composition.</summary>
public sealed class ChatRunRepositoryRouter(IPersistentChatRunRepository durable,
    IHostLifetimeChatRunRepository hostLifetime, IChatRepository chats, Func<IChatKindPolicyRegistry> kinds)
    : IChatRunRepository
{
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ChatId), ChatPersistence> _locations = new();

    public async Task<ChatRunState?> GetAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken)
    {
        if (await hostLifetime.GetAsync(projectId, chatId, branchId, cancellationToken) is { } transient)
        {
            _locations[(projectId, chatId)] = ChatPersistence.HostLifetime;
            return transient;
        }
        return await durable.GetAsync(projectId, chatId, branchId, cancellationToken);
    }

    public async Task SaveAsync(ChatRunState state, CancellationToken cancellationToken)
    {
        var location = await LocationAsync(state.ProjectId, state.ChatId, cancellationToken);
        IChatRunRepository backend = location == ChatPersistence.HostLifetime ? hostLifetime : durable;
        await backend.SaveAsync(state, cancellationToken);
    }

    public async Task<IReadOnlyList<ChatRunState>> ListAsync(CancellationToken cancellationToken)
    {
        var transient = await hostLifetime.ListAsync(cancellationToken);
        foreach (var state in transient) _locations[(state.ProjectId, state.ChatId)] = ChatPersistence.HostLifetime;
        return [.. await durable.ListAsync(cancellationToken), .. transient];
    }

    public async Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await durable.DeleteProjectAsync(projectId, cancellationToken);
        await hostLifetime.DeleteProjectAsync(projectId, cancellationToken);
        foreach (var key in _locations.Keys.Where(key => key.ProjectId == projectId)) _locations.TryRemove(key, out _);
    }

    public async Task DeleteChatAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        await durable.DeleteChatAsync(projectId, chatId, cancellationToken);
        await hostLifetime.DeleteChatAsync(projectId, chatId, cancellationToken);
        _locations.TryRemove((projectId, chatId), out _);
    }

    public async Task DeleteExceptAsync(Guid projectId, Guid chatId, IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken)
    {
        await durable.DeleteExceptAsync(projectId, chatId, branchIds, cancellationToken);
        await hostLifetime.DeleteExceptAsync(projectId, chatId, branchIds, cancellationToken);
    }

    private async Task<ChatPersistence> LocationAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        if (_locations.TryGetValue((projectId, chatId), out var location)) return location;
        var chat = await chats.GetAsync(new ProjectId(projectId), new ChatId(chatId), cancellationToken)
            ?? throw new InvalidOperationException("Chat not found for run storage.");
        location = kinds().Resolve(chat.Chat.Kind).Behavior.Persistence;
        return _locations.GetOrAdd((projectId, chatId), location);
    }
}

public sealed class HostLifetimeChatRunRepository : IHostLifetimeChatRunRepository
{
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ChatId, Guid BranchId), ChatRunState> _runs = new();

    public Task<ChatRunState?> GetAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_runs.TryGetValue((projectId, chatId, branchId), out var state) ? Copy(state) : null);
    }

    public Task SaveAsync(ChatRunState state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _runs[(state.ProjectId, state.ChatId, state.BranchId)] = Copy(state);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ChatRunState>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ChatRunState>>(_runs.Values.Select(Copy).ToArray());
    }

    public Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken) => DeleteAsync(key => key.ProjectId == projectId, cancellationToken);
    public Task DeleteChatAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken) =>
        DeleteAsync(key => key.ProjectId == projectId && key.ChatId == chatId, cancellationToken);
    public Task DeleteExceptAsync(Guid projectId, Guid chatId, IReadOnlySet<Guid> branchIds, CancellationToken cancellationToken) =>
        DeleteAsync(key => key.ProjectId == projectId && key.ChatId == chatId && !branchIds.Contains(key.BranchId), cancellationToken);

    private Task DeleteAsync(Func<(Guid ProjectId, Guid ChatId, Guid BranchId), bool> match, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var key in _runs.Keys.Where(match)) _runs.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    private static ChatRunState Copy(ChatRunState state) => ChatRunState.Restore(state.ProjectId,
        state.ChatId, state.BranchId, state.Status, state.StreamingContent, state.Error, state.FailureKind,
        state.HasUnreadResponse, state.Revision, state.Queue, state.Operations);
}
