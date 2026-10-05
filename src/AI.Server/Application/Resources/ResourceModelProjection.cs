namespace AI.Application.Resources;

using AI.Contracts.Resources;
using System.Text.Json;

public sealed class ResourceModelProjection(IReviewService? reviews, IResourceAssetService? assets) : IResourceModelProjection
{
    // The sync projection remains useful for simple file references and tests. Running chat
    // requests use ProjectAsync so mutable review comments are resolved at request time.
    public ResourceModelProjection() : this(null, null) { }
    public ResourceModelProjection(IReviewService reviews) : this(reviews, null) { }

    public string Project(string content, IReadOnlyList<ChatResource>? references)
    {
        if (references is null or { Count: 0 }) return content;
        content = WithInvokedSkill(content, references);
        references = references.Where(item => item.Kind != ChatResourceKind.Skill).ToArray();
        if (references.Count == 0) return content;
        var lines = references.SelectMany(item =>
            item.Kind == ChatResourceKind.Review
                ? [$"- review: {JsonSerializer.Serialize(item.Name ?? item.Path)} [resource {item.Id}]"]
                : Describe(item));
        return string.Join('\n', (new[] { content, "Attached resources:" }).Concat(lines)
            .Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    public async Task<string> ProjectAsync(Guid projectId, Guid chatId, string content,
        IReadOnlyList<ChatResource>? references, CancellationToken cancellationToken)
    {
        if (references is null or { Count: 0 }) return content;
        content = WithInvokedSkill(content, references);
        references = references.Where(item => item.Kind != ChatResourceKind.Skill).ToArray();
        if (references.Count == 0) return content;
        var reviewItems = references.Any(item => item.Kind == ChatResourceKind.Review)
            ? (await (reviews ?? throw new InvalidOperationException("Review service is required."))
                .ListAsync(projectId, chatId, cancellationToken)).ToDictionary(item => item.Id)
            : [];
        var lines = new List<string>();
        foreach (var reference in references)
        {
            if (reference.Kind != ChatResourceKind.Review)
            {
                lines.AddRange(Describe(reference));
                if (reference.Kind == ChatResourceKind.File && reference.AssetId is { } assetId)
                {
                    var text = await (assets ?? throw new InvalidOperationException("Asset service is required."))
                        .ReadTextAsync(projectId, assetId, cancellationToken);
                    if (text is null) lines.Add("  Binary content is not available as text to this model.");
                    else
                    {
                        lines.AddRange(Fenced(text.Text, string.Empty));
                        if (text.Truncated) lines.Add("  Only the first 64 KiB of this file are shown.");
                    }
                }
                continue;
            }
            if (!reviewItems.TryGetValue(reference.Id, out var review))
            {
                lines.Add($"- review {reference.Id}: unavailable");
                continue;
            }
            // A review emptied after it was sent has nothing to act on. Listing it anyway made the
            // model treat the old message as a pending, contentless request and ask what to do.
            if (review.Kind == ChatReviewKind.Message ? review.MessageComments is not { Count: > 0 }
                : review.Comments.Count == 0)
                continue;
            if (review.Kind == ChatReviewKind.Message)
            {
                lines.Add($"- message review: {JsonSerializer.Serialize(review.Name)} [resource {review.Id}; source message {review.SourceMessageId}; current mutable state]");
                foreach (var comment in (review.MessageComments ?? []).Take(10))
                    lines.Add($"  - selected text {JsonSerializer.Serialize(comment.Quote)}: {JsonSerializer.Serialize(comment.Body[..Math.Min(comment.Body.Length, 500)])}");
                if ((review.MessageComments?.Count ?? 0) > 10)
                    lines.Add($"  - {review.MessageComments!.Count - 10} more comments; use app_read to inspect the resource.");
                continue;
            }
            lines.Add($"- review: {JsonSerializer.Serialize(review.Name)}{Linked(reference)} [resource {review.Id}; saved changes in message {review.SourceMessageId}; current mutable state]");
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
        if (lines.Count == 0) return content;
        return string.Join('\n', new[] { content, "Attached resources:" }.Concat(lines)
            .Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    /// <summary>
    /// Everything but a review, which needs the live review state. Files and directories stay live
    /// paths; a line range and uncommitted changes carry the text captured when the message was sent.
    /// </summary>
    private static IEnumerable<string> Describe(ChatResource reference)
    {
        var name = JsonSerializer.Serialize(reference.Name ?? reference.Path);
        switch (reference.Kind)
        {
            case ChatResourceKind.Image:
                yield return $"- image: {name}{Linked(reference)} [resource {reference.Id}; attached image; "
                             + $"source {reference.Source}: {JsonSerializer.Serialize(reference.Path)}]";
                yield break;
            case ChatResourceKind.File when reference.AssetId is not null:
                yield return $"- uploaded file: {name}{Linked(reference)} [resource {reference.Id}; "
                             + $"media type {reference.MediaType}; {reference.Size} bytes; saved content follows when readable]";
                yield break;
            case ChatResourceKind.Chat:
                yield return $"- chat: {name}{Linked(reference)} [chat {reference.Path} in this project; read its messages with app_read "
                             + $"resource Messages and chatId {reference.Path}]";
                yield break;
            case ChatResourceKind.Project:
                yield return $"- project: {name}{Linked(reference)} [project {reference.Path}; read it with app_read resource Project "
                             + $"and projectId {reference.Path}, its chats with Chats]";
                yield break;
            case ChatResourceKind.Diff:
                yield return $"- uncommitted changes: {JsonSerializer.Serialize(reference.Path)}{Linked(reference)} [resource {reference.Id}; "
                             + "git diff against HEAD captured when the message was sent; paths relative to that directory]";
                foreach (var line in Fenced(reference.Excerpt ?? "No uncommitted changes.", "diff")) yield return line;
                yield break;
            case ChatResourceKind.File when reference.Lines is { } range:
                yield return $"- file: {JsonSerializer.Serialize(reference.Path)} lines {range.Start}-{range.End}{Linked(reference)} "
                             + $"[resource {reference.Id}; these lines as they were when the message was sent]";
                if (reference.Excerpt is { } excerpt) foreach (var line in Fenced(excerpt, string.Empty)) yield return line;
                yield break;
            default:
                yield return $"- {reference.Kind.ToString().ToLowerInvariant()}: {JsonSerializer.Serialize(reference.Path)}{Linked(reference)} "
                             + $"[resource {reference.Id}; live path; contents not loaded]";
                yield break;
        }
    }

    /// <summary>Which "@" link in the text the reference is, so the model can tell them apart.</summary>
    private static string Linked(ChatResource reference) =>
        reference.Mention is { } mention ? $" (linked in the message as {JsonSerializer.Serialize(mention)})" : string.Empty;

    /// <summary>A fence longer than any backtick run inside, so captured text cannot close it early.</summary>
    private static IEnumerable<string> Fenced(string text, string language)
    {
        var longest = 0;
        var run = 0;
        foreach (var character in text)
        {
            run = character == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }
        var fence = new string('`', Math.Max(3, longest + 1));
        yield return $"  {fence}{language}";
        foreach (var line in text.TrimEnd('\n').Split('\n')) yield return $"  {line.TrimEnd('\r')}";
        yield return $"  {fence}";
    }

    /// <summary>
    /// A skill chosen from the composer's slash list. The message text is the user's instructions
    /// for it; the model still prepares the parameters with its ordinary tools and runs it.
    /// </summary>
    private static string WithInvokedSkill(string content, IReadOnlyList<ChatResource> references)
    {
        if (references.FirstOrDefault(item => item.Kind == ChatResourceKind.Skill) is not { } skill) return content;
        var line = $"The user invoked the skill {JsonSerializer.Serialize(skill.Path)}"
                   + (skill.Name is { Length: > 0 } name ? $" ({JsonSerializer.Serialize(name)})" : string.Empty)
                   + " for this message. Run it now with mcp_app__run_skill, taking its parameters from the message; use "
                   + "mcp_app__skill_search for its schema if you do not have it. A playbook returns instructions: follow them in this turn.";
        return string.IsNullOrWhiteSpace(content) ? line : $"{line}\n{content}";
    }
}
