namespace AI.Contracts.Navigation;

/// <summary>
/// A request from the Host to show a project, a chat or a branch, as if the user had picked it.
/// Sent on the run event stream as <c>event: navigate</c>; the chat and branch are optional.
/// </summary>
public sealed record AppNavigation(Guid ProjectId, Guid? ChatId = null, Guid? BranchId = null);
