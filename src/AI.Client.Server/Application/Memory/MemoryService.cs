namespace AI.Client.Application.Memory;

using AI.Client.Contracts.Memory;
using Projects;

public sealed class MemoryService(IMemoryRepository repository, IProjectService projects, IIdGenerator ids, IClock clock)
    : IMemoryService
{
    public const int TitleLimit = 120;
    public const int BodyLimit = 4_000;
    public const int TagLimit = 8;
    public const int TagLengthLimit = 32;
    public const int CatalogCapacity = 300;

    public async Task<IReadOnlyList<MemoryEntry>> ListAsync(Guid? projectId, CancellationToken cancellationToken)
    {
        var user = await repository.ListAsync(null, cancellationToken);
        if (projectId is not { } id) return user;
        return [.. user, .. await repository.ListAsync(id, cancellationToken)];
    }

    public async Task<MemoryEntry?> GetAsync(Guid id, Guid? projectId, CancellationToken cancellationToken) =>
        (await ListAsync(projectId, cancellationToken)).SingleOrDefault(entry => entry.Id == id);

    public async Task<IReadOnlyList<MemoryEntry>> SearchAsync(string query, Guid? projectId, CancellationToken cancellationToken)
    {
        var words = (query ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) throw new ArgumentException("'query' is required.");
        return (await ListAsync(projectId, cancellationToken))
            .Where(entry => entry.Enabled)
            .Select(entry => (Entry: entry, Score: Score(entry, words)))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Entry.UpdatedAt)
            .Select(item => item.Entry)
            .ToArray();
    }

    public async Task<MemoryWriteResult> CreateAsync(CreateMemoryEntryRequest request, MemoryAuthor author, Guid? chatId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Validate(request.Title, request.Body, request.Tags) is { } error) return MemoryWriteResult.Rejected(error);
        var owner = await OwnerAsync(request.Scope, request.ProjectId, cancellationToken);
        var now = clock.UtcNow;
        var entry = new MemoryEntry(ids.Create(), request.Scope, owner, request.Kind, request.Title.Trim(),
            request.Body.Trim(), Tags(request.Tags), request.Pinned, true, author,
            author == MemoryAuthor.Model ? chatId : null, now, now, 1);
        return await repository.AddAsync(owner, entry, CatalogCapacity, cancellationToken);
    }

    public async Task<MemoryWriteResult> UpdateAsync(Guid id, Guid? projectId, UpdateMemoryEntryRequest request,
        MemoryAuthor author, Guid? chatId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Validate(request.Title, request.Body, request.Tags) is { } error) return MemoryWriteResult.Rejected(error);
        if (await OwnerOfAsync(id, projectId, cancellationToken) is not { } owner) return MemoryWriteResult.NotFound();
        var now = clock.UtcNow;
        return await repository.UpdateAsync(owner.ProjectId, id, request.Revision, entry => entry with
        {
            Kind = request.Kind,
            Title = request.Title.Trim(),
            Body = request.Body.Trim(),
            Tags = Tags(request.Tags),
            Pinned = request.Pinned,
            Enabled = request.Enabled,
            Author = author,
            ChatId = author == MemoryAuthor.Model ? chatId : entry.ChatId,
            UpdatedAt = now < entry.UpdatedAt ? entry.UpdatedAt : now,
            Revision = checked(entry.Revision + 1)
        }, cancellationToken);
    }

    public async Task<MemoryWriteResult> DeleteAsync(Guid id, Guid? projectId, long expectedRevision,
        CancellationToken cancellationToken) =>
        await OwnerOfAsync(id, projectId, cancellationToken) is { } owner
            ? await repository.DeleteAsync(owner.ProjectId, id, expectedRevision, cancellationToken)
            : MemoryWriteResult.NotFound();

    public Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        repository.DeleteProjectAsync(projectId, cancellationToken);

    /// <summary>Which catalog holds the entry; the person's own is searched first.</summary>
    private async Task<Owner?> OwnerOfAsync(Guid id, Guid? projectId, CancellationToken cancellationToken)
    {
        if ((await repository.ListAsync(null, cancellationToken)).Any(entry => entry.Id == id))
            return new Owner(null);
        if (projectId is { } project && (await repository.ListAsync(project, cancellationToken)).Any(entry => entry.Id == id))
            return new Owner(project);
        return null;
    }

    private async Task<Guid?> OwnerAsync(MemoryScope scope, Guid? projectId, CancellationToken cancellationToken)
    {
        if (scope == MemoryScope.User) return null;
        if (projectId is not { } id || id == Guid.Empty)
            throw new ArgumentException("A project memory entry needs 'projectId'.");
        _ = await projects.GetAsync(id, cancellationToken) ?? throw new ArgumentException("Project not found.");
        return id;
    }

    private static string? Validate(string? title, string? body, IReadOnlyList<string>? tags)
    {
        if (string.IsNullOrWhiteSpace(title)) return "'title' cannot be empty.";
        if (title.Trim().Length > TitleLimit) return $"'title' is longer than {TitleLimit} characters.";
        if (body is null) return "'body' is required.";
        if (body.Trim().Length > BodyLimit) return $"'body' is longer than {BodyLimit} characters; keep one fact per entry.";
        if (tags is { Count: > TagLimit }) return $"No more than {TagLimit} tags.";
        if (tags?.Any(tag => tag.Trim().Length > TagLengthLimit) == true)
            return $"A tag is longer than {TagLengthLimit} characters.";
        return null;
    }

    private static string[] Tags(IReadOnlyList<string>? tags) =>
        tags is null ? [] : tags.Select(tag => tag.Trim()).Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static int Score(MemoryEntry entry, IReadOnlyList<string> words)
    {
        var score = 0;
        foreach (var word in words)
        {
            var inTitle = entry.Title.Contains(word, StringComparison.OrdinalIgnoreCase);
            var inTags = entry.Tags.Any(tag => tag.Contains(word, StringComparison.OrdinalIgnoreCase));
            var inBody = entry.Body.Contains(word, StringComparison.OrdinalIgnoreCase);
            if (!inTitle && !inTags && !inBody) return 0;
            score += (inTitle ? 3 : 0) + (inTags ? 2 : 0) + (inBody ? 1 : 0);
        }
        return score;
    }

    private sealed record Owner(Guid? ProjectId);
}
