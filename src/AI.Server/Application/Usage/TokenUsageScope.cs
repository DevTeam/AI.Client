namespace AI.Application.Usage;

using AI.Contracts.Usage;

/// <summary>
/// What the requests made inside a scope are for. Every field left null is taken from the scope
/// around it, so a run names its chat and turn once and the steps inside it only say why they
/// call the model.
/// </summary>
/// <param name="Observer">
/// Told about every request measured inside the scope, including those of nested scopes: a turn
/// hears about its subtasks' requests too.
/// </param>
public sealed record TokenUsageScope(
    TokenUsagePurpose? Purpose = null,
    Guid? ProjectId = null,
    Guid? ChatId = null,
    Guid? BranchId = null,
    Guid? TurnId = null,
    Func<TokenUsageRecord, CancellationToken, Task>? Observer = null);
