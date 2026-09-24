namespace AI.Client.Infrastructure.Storage;

using System.Text.Json;
using AI.Client.Application.Resources;
using AI.Client.Contracts.Resources;

/// <summary>One small catalog per project; the chat ID is checked on every read and write.</summary>
public sealed class JsonReviewRepository(IProjectStorageLocation location, ITextFileSystem files) : IReviewRepository, IDisposable
{
    private readonly AsyncGate _gate = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public void Dispose() => _gate.Dispose();

    public async Task<IReadOnlyList<ChatReview>> ListAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        using var lease = await _gate.EnterAsync(cancellationToken);
        return (await LoadAsync(projectId, cancellationToken)).Where(item => item.ChatId == chatId).ToArray();
    }

    public async Task<ChatReview> CreateAsync(ChatReview review, CancellationToken cancellationToken)
    {
        using var lease = await _gate.EnterAsync(cancellationToken);
        var items = (await LoadAsync(review.ProjectId, cancellationToken)).ToList();
        if (items.Any(item => item.Id == review.Id)) throw new InvalidOperationException("Review already exists.");
        items.Add(review);
        await SaveAsync(review.ProjectId, items, cancellationToken);
        return review;
    }

    public async Task<ChatReview?> UpdateAsync(Guid projectId, Guid chatId, Guid reviewId, long expectedRevision,
        Func<ChatReview, ChatReview> update, CancellationToken cancellationToken)
    {
        using var lease = await _gate.EnterAsync(cancellationToken);
        var items = (await LoadAsync(projectId, cancellationToken)).ToList();
        var index = items.FindIndex(item => item.ChatId == chatId && item.Id == reviewId);
        if (index < 0) return null;
        if (items[index].Revision != expectedRevision) throw new InvalidOperationException("Review changed. Reload it before saving.");
        var revised = update(items[index]) with { Revision = checked(expectedRevision + 1) };
        items[index] = revised;
        await SaveAsync(projectId, items, cancellationToken);
        return revised;
    }

    public async Task DeleteChatAsync(Guid projectId, Guid chatId, CancellationToken cancellationToken)
    {
        using var lease = await _gate.EnterAsync(cancellationToken);
        var items = (await LoadAsync(projectId, cancellationToken)).Where(item => item.ChatId != chatId).ToArray();
        await SaveAsync(projectId, items, cancellationToken);
    }

    public async Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        using var lease = await _gate.EnterAsync(cancellationToken);
        await files.DeleteAsync(PathFor(projectId), cancellationToken);
    }

    private async Task<IReadOnlyList<ChatReview>> LoadAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var text = await files.ReadTextAsync(PathFor(projectId), cancellationToken);
        if (text is null) return [];
        var document = JsonSerializer.Deserialize<ReviewDocument>(text)
            ?? throw new JsonException("Invalid review catalog.");
        if (document.SchemaVersion != 1) throw new JsonException("Unsupported review catalog schema.");
        return document.Reviews;
    }

    private async Task SaveAsync(Guid projectId, IReadOnlyList<ChatReview> reviews, CancellationToken cancellationToken)
    {
        var path = PathFor(projectId);
        var temporary = path + ".tmp";
        await files.WriteTextAsync(temporary, JsonSerializer.Serialize(new ReviewDocument(1, reviews), Json), cancellationToken);
        await files.MoveAsync(temporary, path, true, cancellationToken);
    }

    private string PathFor(Guid projectId) => Path.Combine(location.RootDirectory, "resources", $"reviews-{projectId}.json");
    private sealed record ReviewDocument(int SchemaVersion, IReadOnlyList<ChatReview> Reviews);
}
