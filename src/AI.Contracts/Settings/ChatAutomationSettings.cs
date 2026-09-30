namespace AI.Contracts.Settings;

/// <summary>
/// The model calls the Host makes on its own around an answer, beside the ones the user asked for.
/// Each costs a request to the chat's connection, so each can be switched off.
/// </summary>
/// <param name="AutoTitle">Name a new chat after its first answer (chat-rename).</param>
/// <param name="SuggestReplies">Draft the user's likely next message after each answer (chat-reply-suggest).</param>
/// <param name="RouteSkills">Pick the skills and first tools for each new message before the model's first step (skill-route).</param>
public sealed record ChatAutomationSettings(bool AutoTitle = true, bool SuggestReplies = true, bool RouteSkills = true);
