namespace AI.Contracts.Chats;

/// <summary>
/// How a chat answers a tool call that its standing policy leaves at <c>Ask</c>. An explicit
/// <c>Deny</c> and the per-run call limits hold in every mode: the mode only decides who answers
/// the question the policy would otherwise put to the person.
/// </summary>
public enum ToolApprovalMode
{
    /// <summary>Every such call waits for the person's confirmation.</summary>
    Ask,

    /// <summary>
    /// The <c>chat-tool-risk-assess</c> skill judges the call first; only a call it finds safe runs
    /// without a card, and anything else — including an assessment that fails — is still asked.
    /// </summary>
    Auto,

    /// <summary>Every such call runs without asking.</summary>
    FullAccess
}
