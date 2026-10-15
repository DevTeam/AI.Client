namespace AI.Contracts.Settings;

/// <summary>
/// A permission rule for the programs <c>process_run</c> starts: a pattern such as
/// <c>git diff *</c> and what to do with a call that matches it.
/// </summary>
/// <param name="Scope"><see cref="Global"/>, <see cref="Project"/> or <see cref="Chat"/>.</param>
/// <param name="ProjectId">The project a Project or Chat rule belongs to; null for a Global one.</param>
/// <param name="ChatId">The chat a Chat rule belongs to; null otherwise.</param>
/// <param name="Pattern">The program's name, then words matched one by one; see docs/06-security.md.</param>
/// <param name="Decision">Allow, Ask or Deny.</param>
public sealed record CommandRule(
    Guid Id,
    string Scope,
    Guid? ProjectId,
    Guid? ChatId,
    string Pattern,
    string Decision,
    DateTimeOffset CreatedAt)
{
    public const string Global = "Global";
    public const string Project = "Project";
    public const string Chat = "Chat";
}

public sealed record CreateCommandRuleRequest(string Scope, Guid? ProjectId, Guid? ChatId, string Pattern, string Decision);
