namespace AI.Contracts.Navigation;

/// <summary>
/// A request from the Host to show a project, a chat or a branch, as if the user had picked it.
/// Sent on the run event stream as <c>event: navigate</c>; the chat and branch are optional.
/// </summary>
/// <param name="SourceChatId">
/// The chat whose run asked for it. A window follows on its own only while it shows that chat:
/// anyone looking elsewhere is offered the move instead of being taken away from what they read.
/// </param>
/// <param name="ProjectName">The target's names, for the words that tell the user what happened.</param>
public sealed record AppNavigation(Guid ProjectId, Guid? ChatId = null, Guid? BranchId = null,
    Guid? SourceChatId = null, string? ProjectName = null, string? ChatTitle = null,
    string? Target = null, string Action = "click", string? Comment = null,
    bool WaitForContinue = false, bool WaitForUser = false, string? Value = null,
    Guid RequestId = default, DateTimeOffset? ExpiresAt = null,
    string? SkillId = null, string? ToolName = null)
{
    public const int DefaultTimeoutSeconds = 15;
}

public sealed record AppNavigationResponse(string Outcome, string? Error = null, IReadOnlyList<AppNavigationTarget>? Targets = null);
public sealed record AppNavigationDecision(Guid ClientId, string Outcome, string? Error = null, IReadOnlyList<AppNavigationTarget>? Targets = null);
