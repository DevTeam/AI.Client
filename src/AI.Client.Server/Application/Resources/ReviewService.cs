namespace AI.Client.Application.Resources;

using AI.Client.Application.Chats;
using AI.Client.Contracts.Resources;
using AI.Client.Contracts.Workspace;

/// <summary>Owns mutable reviews; their source is always a saved change set in the same chat.</summary>
public sealed class ReviewService(IChatService chats, IReviewRepository repository,
    IUnifiedDiffParser diffParser) : IReviewService
{
    public async Task<IReadOnlyList<ChatReview>> ListAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        await RequireChatAsync(projectId, chatId, cancellationToken);
        return (await repository.ListAsync(projectId, chatId, cancellationToken))
            .OrderByDescending(item => item.SourceCreatedAt).ThenByDescending(item => item.CreatedAt).ToArray();
    }

    public async Task<ChatReview?> GetAsync(Guid projectId, Guid chatId, Guid reviewId, CancellationToken cancellationToken) =>
        (await ListAsync(projectId, chatId, cancellationToken)).FirstOrDefault(item => item.Id == reviewId);

    public async Task<ChatReview> CreateAsync(Guid projectId, Guid chatId, CreateReviewRequest request, CancellationToken cancellationToken)
    {
        var chat = await RequireChatAsync(projectId, chatId, cancellationToken);
        var source = chat.Messages.FirstOrDefault(item => item.Id == request.SourceMessageId)
            ?? throw new ArgumentException("Review source message does not exist in this chat.");
        if (source.Role != "Assistant" || source.WorkspaceChanges is not { IsEmpty: false } changes)
            throw new ArgumentException("Review source has no saved file changes.");
        var name = CleanName(request.Name);
        var files = ValidateFiles(request.Files, changes);
        var now = DateTimeOffset.UtcNow;
        return await repository.CreateAsync(new ChatReview(Guid.CreateVersion7(), projectId, chatId, name,
            source.Id, source.CreatedAt, files, [], now, now, 1), cancellationToken);
    }

    public async Task<ChatReview?> UpdateAsync(Guid projectId, Guid chatId, Guid reviewId,
        UpdateReviewRequest request, CancellationToken cancellationToken)
    {
        var chat = await RequireChatAsync(projectId, chatId, cancellationToken);
        var review = (await repository.ListAsync(projectId, chatId, cancellationToken)).FirstOrDefault(item => item.Id == reviewId);
        if (review is null) return null;
        var source = chat.Messages.FirstOrDefault(item => item.Id == review.SourceMessageId);
        var changes = source?.WorkspaceChanges ?? throw new InvalidOperationException("Review source is unavailable.");
        var name = CleanName(request.Name);
        var files = ValidateFiles(request.Files, changes);
        var comments = ValidateComments(request.Comments, changes, files);
        return await repository.UpdateAsync(projectId, chatId, reviewId, request.ExpectedRevision,
            current => current with { Name = name, Files = files, Comments = comments, UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken);
    }

    public Task DeleteChatAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken) =>
        repository.DeleteChatAsync(projectId, chatId, cancellationToken);

    public Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken) => repository.DeleteProjectAsync(projectId, cancellationToken);

    private async Task<AI.Client.Contracts.Chats.ChatDetails> RequireChatAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken) =>
        await chats.GetAsync(projectId, chatId, cancellationToken) ?? throw new InvalidOperationException("Chat not found.");

    private static string CleanName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is < 1 or > 120) throw new ArgumentException("Review name must contain 1 to 120 characters.");
        return trimmed;
    }

    private static string[] ValidateFiles(IReadOnlyList<string>? files, WorkspaceChangeSet changes)
    {
        if (files is null or { Count: 0 } || files.Count > 200 || files.Distinct(StringComparer.Ordinal).Count() != files.Count)
            throw new ArgumentException("Review must select 1 to 200 distinct files.");
        var available = changes.Files.Select(item => item.Path).ToHashSet(StringComparer.Ordinal);
        if (files.Any(path => !available.Contains(path))) throw new ArgumentException("Review file is not in the saved change set.");
        return files.ToArray();
    }

    private ReviewComment[] ValidateComments(IReadOnlyList<ReviewComment>? comments,
        WorkspaceChangeSet changes, IReadOnlyList<string> files)
    {
        if (comments is null || comments.Count > 200 || comments.Select(item => item.Id).Distinct().Count() != comments.Count)
            throw new ArgumentException("Review has too many or duplicate comments.");
        var selected = files.ToHashSet(StringComparer.Ordinal);
        foreach (var comment in comments)
        {
            var file = changes.Files.FirstOrDefault(item => item.Path == comment.Path);
            if (comment.Id == Guid.Empty || file is null || !selected.Contains(comment.Path)
                || string.IsNullOrWhiteSpace(comment.Body) || comment.Body.Length > 8000)
                throw new ArgumentException("Review comment has an invalid file or body.");
            var hasOld = comment.OldStart is not null || comment.OldEnd is not null;
            var hasNew = comment.NewStart is not null || comment.NewEnd is not null;
            if (hasOld && hasNew || hasOld && (comment.OldStart is not > 0 || comment.OldEnd is null || comment.OldEnd < comment.OldStart)
                || hasNew && (comment.NewStart is not > 0 || comment.NewEnd is null || comment.NewEnd < comment.NewStart)
                || (hasOld || hasNew) && string.IsNullOrEmpty(file.Diff))
                throw new ArgumentException("Review comment has an invalid line anchor.");
            if (hasOld || hasNew)
            {
                var start = (comment.OldStart ?? comment.NewStart)!.Value;
                var end = (comment.OldEnd ?? comment.NewEnd)!.Value;
                if (end - start > 100) throw new ArgumentException("Review comment range is too long.");
                var visible = diffParser.Parse(file.Diff)
                    .Where(line => line.Kind is DiffLineKind.Added or DiffLineKind.Removed or DiffLineKind.Context)
                    .Select(line => hasOld ? line.OldLine : line.NewLine)
                    .OfType<int>().ToHashSet();
                for (var number = start; number <= end; number++)
                    if (!visible.Contains(number)) throw new ArgumentException("Review comment line is outside the saved diff.");
            }
        }
        return comments.ToArray();
    }
}
