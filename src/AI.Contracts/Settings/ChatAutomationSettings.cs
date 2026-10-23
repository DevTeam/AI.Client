namespace AI.Contracts.Settings;

/// <summary>
/// The model calls the Host makes on its own around an answer, beside the ones the user asked for.
/// Each costs a request to the chat's connection, so each can be switched off.
/// </summary>
/// <param name="AutoTitle">Name a new chat after its first answer (chat-rename).</param>
/// <param name="SuggestReplies">Draft the user's likely next message after each answer (chat-reply-suggest).</param>
/// <param name="SuggestComments">Draft a review comment when the user opens a comment box (chat-comment-suggest).</param>
/// <param name="RouteSkills">Pick the skills and first tools for each new message before the model's first step (skill-route).</param>
/// <param name="MaxSubtaskDepth">
/// How many levels of delegated work may nest. At 1 a chat may delegate and its subtasks may not;
/// each further level multiplies requests and slots, so going deeper is the user's choice.
/// </param>
public sealed record ChatAutomationSettings(bool AutoTitle = true, bool SuggestReplies = true, bool RouteSkills = true,
    bool SuggestComments = true, int MaxSubtaskDepth = 1);
