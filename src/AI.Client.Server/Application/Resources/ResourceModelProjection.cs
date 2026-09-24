namespace AI.Client.Application.Resources;

using AI.Client.Contracts.Resources;
using System.Text.Json;

public sealed class ResourceModelProjection(IReviewService? reviews) : IResourceModelProjection
{
    // The sync projection remains useful for simple file references and tests. Running chat
    // requests use ProjectAsync so mutable review comments are resolved at request time.
    public ResourceModelProjection() : this(null) { }

    public string Project(string content, IReadOnlyList<ChatResourceRef>? references)
    {
        if (references is null or { Count: 0 }) return content;
        var lines = references.Select(item =>
            item.Kind == ChatResourceKind.Review
                ? $"- review: {JsonSerializer.Serialize(item.Name ?? item.Path)} [resource {item.Id}]"
                : $"- {item.Kind.ToString().ToLowerInvariant()}: {JsonSerializer.Serialize(item.Path)} [resource {item.Id}; live path; contents not loaded]");
        return string.Join('\n', (new[] { content, "Attached workspace references:" }).Concat(lines)
            .Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    public async Task<string> ProjectAsync(Guid projectId, Guid chatId, string content,
        IReadOnlyList<ChatResourceRef>? references, CancellationToken cancellationToken)
    {
        if (references is null or { Count: 0 }) return content;
        var reviewItems = references.Any(item => item.Kind == ChatResourceKind.Review)
            ? (await (reviews ?? throw new InvalidOperationException("Review service is required."))
                .ListAsync(projectId, chatId, cancellationToken)).ToDictionary(item => item.Id)
            : [];
        var lines = new List<string> { content, "Attached workspace references:" };
        foreach (var reference in references)
        {
            if (reference.Kind != ChatResourceKind.Review)
            {
                lines.Add($"- {reference.Kind.ToString().ToLowerInvariant()}: {JsonSerializer.Serialize(reference.Path)} [resource {reference.Id}; live path; contents not loaded]");
                continue;
            }
            if (!reviewItems.TryGetValue(reference.Id, out var review))
            {
                lines.Add($"- review {reference.Id}: unavailable");
                continue;
            }
            if (review.Kind == ChatReviewKind.Message)
            {
                lines.Add($"- message review: {JsonSerializer.Serialize(review.Name)} [resource {review.Id}; source message {review.SourceMessageId}; current mutable state]");
                foreach (var comment in (review.MessageComments ?? []).Take(10))
                    lines.Add($"  - selected text {JsonSerializer.Serialize(comment.Quote)}: {JsonSerializer.Serialize(comment.Body[..Math.Min(comment.Body.Length, 500)])}");
                if ((review.MessageComments?.Count ?? 0) > 10)
                    lines.Add($"  - {review.MessageComments!.Count - 10} more comments; use app_read to inspect the resource.");
                continue;
            }
            lines.Add($"- review: {JsonSerializer.Serialize(review.Name)} [resource {review.Id}; saved changes in message {review.SourceMessageId}; current mutable state]");
            foreach (var file in review.Files.Take(20)) lines.Add($"  - selected file: {JsonSerializer.Serialize(file)}");
            foreach (var comment in review.Comments.Take(10))
            {
                var anchor = comment.NewStart is { } newer ? $"new lines {newer}-{comment.NewEnd}"
                    : comment.OldStart is { } older ? $"old lines {older}-{comment.OldEnd}" : "whole file";
                lines.Add($"  - {JsonSerializer.Serialize(comment.Path)}, {anchor}: {JsonSerializer.Serialize(comment.Body[..Math.Min(comment.Body.Length, 500)])}");
            }
            if (review.Files.Count > 20) lines.Add($"  - {review.Files.Count - 20} more files; use app_read to inspect the resource.");
            if (review.Comments.Count > 10) lines.Add($"  - {review.Comments.Count - 10} more comments; use app_read to inspect the resource.");
        }
        return string.Join('\n', lines.Where(line => !string.IsNullOrWhiteSpace(line)));
    }
}
