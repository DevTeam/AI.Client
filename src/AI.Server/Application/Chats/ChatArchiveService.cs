namespace AI.Application.Chats;

using AI.Application.Runs;
using AI.Application.Projects;
using AI.Contracts.Chats;
using AI.Contracts.Runs;
using AI.Domain.Chats;
using AI.Domain.Projects;

public interface IChatArchiveService
{
    Task<ChatArchivePreview> PreviewAsync(Guid projectId, ChatArchivePreviewRequest request, CancellationToken token);
    Task<ChatArchiveResult> ApplyAsync(Guid projectId, ChatArchiveRequest request, CancellationToken token);
    Task<ChatArchiveResult> UndoAsync(Guid projectId, Guid operationId, CancellationToken token);
}

/// <summary>Applies only reviewed revisions. Archive operation ids survive restart and identify exactly what Undo restores.</summary>
public sealed class ChatArchiveService(IChatService chats, IChatRepository repository,
    IChatSynchronization synchronization, IClock clock, Func<IChatRunDispatcher> runs) : IChatArchiveService
{
    public async Task<ChatArchivePreview> PreviewAsync(Guid projectId, ChatArchivePreviewRequest request, CancellationToken token)
    {
        var candidates = (await chats.ListAsync(projectId, token))
            .Where(chat => chat.ArchivedAt is null && chat.LastActivityAt < request.Before
                && (request.IncludePinned || !chat.IsPinned)).ToArray();
        var snapshots = await runs().GetSnapshotAsync(token);
        var eligible = candidates.Where(chat => !Busy(snapshots, chat.Id)).ToArray();
        return new ChatArchivePreview(eligible, candidates.Length - eligible.Length, request.Before);
    }

    public async Task<ChatArchiveResult> ApplyAsync(Guid projectId, ChatArchiveRequest request, CancellationToken token)
    {
        if (request.OperationId == Guid.Empty) throw new ArgumentException("An operation id is required.");
        if (request.Targets is null || request.Targets.Count > 500)
            throw new ArgumentException("Select at most 500 chats per operation.");
        if (!request.SkipBusy && request.Targets.Count != 1)
            throw new ArgumentException("Only an explicitly selected single chat can be archived while running.");
        var changed = new List<ChatArchiveTarget>();
        var skipped = new List<ChatArchiveSkip>();
        foreach (var target in request.Targets.DistinctBy(target => target.ChatId))
        {
            using var lease = await synchronization.EnterAsync(target.ChatId, token);
            var stored = await repository.GetAsync(new ProjectId(projectId), new ChatId(target.ChatId), token);
            if (stored is null) { skipped.Add(new(target.ChatId, "Chat no longer exists.")); continue; }
            // A retried request may arrive after some or all changes landed, including after a restart.
            if (request.IsArchived && stored.Chat.ArchiveOperationId == request.OperationId)
            { changed.Add(new(target.ChatId, stored.Revision)); continue; }
            if (stored.Revision != target.Revision)
            { skipped.Add(new(target.ChatId, "Chat changed since the preview.")); continue; }
            if ((stored.Chat.ArchivedAt is not null) == request.IsArchived) continue;
            if (request.IsArchived && request.SkipBusy && Busy(await runs().GetSnapshotAsync(token), target.ChatId))
            { skipped.Add(new(target.ChatId, "Chat is running or needs attention.")); continue; }
            stored.Chat.SetArchived(request.IsArchived, request.OperationId, clock.UtcNow);
            var saved = await repository.SaveAsync(stored.Chat, target.Revision, token);
            if (saved.IsSaved) changed.Add(new(target.ChatId, saved.Revision));
            else skipped.Add(new(target.ChatId, "Chat changed since the preview."));
        }
        return new ChatArchiveResult(request.OperationId, changed, skipped);
    }

    public async Task<ChatArchiveResult> UndoAsync(Guid projectId, Guid operationId, CancellationToken token)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("An archive operation id is required.");
        var targets = (await chats.ListAsync(projectId, token))
            .Where(chat => chat.ArchivedAt is not null && chat.ArchiveOperationId == operationId)
            .Select(chat => new ChatArchiveTarget(chat.Id, chat.Revision)).ToArray();
        var changed = new List<ChatArchiveTarget>();
        var skipped = new List<ChatArchiveSkip>();
        var undoId = Guid.NewGuid();
        foreach (var batch in targets.Chunk(500))
        {
            var result = await ApplyAsync(projectId, new ChatArchiveRequest(false, undoId, batch), token);
            changed.AddRange(result.Changed); skipped.AddRange(result.Skipped);
        }
        return new ChatArchiveResult(undoId, changed, skipped);
    }

    private static bool Busy(IEnumerable<ChatRunSnapshot> snapshots, Guid chatId) => snapshots.Any(run => run.ChatId == chatId
        && (run.Status is ChatRunStatus.Generating or ChatRunStatus.Paused or ChatRunStatus.Interrupted or ChatRunStatus.Failed
            || run.PendingApproval is not null || run.PendingPrompt is not null || run.Wait is not null || run.Queue.Count > 0));
}
