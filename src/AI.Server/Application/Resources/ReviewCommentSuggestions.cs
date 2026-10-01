namespace AI.Application.Resources;

using System.Text.Json;
using AI.Application.Settings;
using AI.Application.Skills;
using AI.Contracts.Resources;
using AI.Contracts.Settings;

/// <summary>
/// Drafts of a review comment, written while the user has the comment box open. Unlike reply
/// drafts nothing is kept: the fragment exists only in the open editor, so each request writes one.
/// </summary>
public interface IReviewCommentSuggestions
{
    /// <summary>
    /// A draft for the fragment in <paramref name="request"/>, or null when there is none to offer,
    /// or the request is automatic and Settings → Chat switched automatic drafts off.
    /// </summary>
    Task<ReviewCommentSuggestion?> SuggestAsync(Guid projectId, Guid chatId, ReviewCommentSuggestionRequest request,
        CancellationToken cancellationToken);
}

public sealed class ReviewCommentSuggestions(ISkillRunner skills, IGlobalSettingsRepository settings) : IReviewCommentSuggestions
{
    public async Task<ReviewCommentSuggestion?> SuggestAsync(Guid projectId, Guid chatId, ReviewCommentSuggestionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Quote)) return null;
        if (request.Automatic
            && !((await settings.LoadAsync(cancellationToken)).ChatAutomation ?? new ChatAutomationSettings()).SuggestComments)
            return null;
        var record = await skills.RunAsync(new SkillInvocation("chat-comment-suggest", projectId,
            JsonSerializer.SerializeToElement(new
            {
                chat_id = chatId,
                quote = request.Quote,
                message_id = request.MessageId,
                path = request.Path,
                diff = request.Diff
            }, IgnoreNulls), chatId), cancellationToken);
        return record is { Status: "Completed", Output: { ValueKind: JsonValueKind.Object } output }
            && output.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
            ? new ReviewCommentSuggestion(text.GetString()!)
            : null;
    }

    // The schema takes no nulls: a fragment without a message or a file leaves those out.
    private static readonly JsonSerializerOptions IgnoreNulls =
        new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
}
