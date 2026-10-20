namespace AI.Agent.Evals;

/// <summary>What one attempt at a scenario did and cost; written as <c>result.json</c> next to its trace.</summary>
/// <param name="Outcome">
/// <c>completed</c>, <c>failed</c>, <c>interrupted</c>, <c>timeout</c> or <c>error</c> (the runner, not the model, failed).
/// </param>
/// <param name="ToolErrors">Tool results marked as errors, programs that merely exited non-zero left out.</param>
/// <param name="NonZeroExits">Programs run through a tool that exited with a non-zero code.</param>
/// <param name="Compactions">Summaries written to fit or trim the context (compaction and checkpoint requests).</param>
/// <param name="RepeatedToolCalls">Calls with the same tool and arguments as an earlier call of the attempt.</param>
/// <param name="Requests">Model requests by purpose, as the usage ledger names them (answer, summary, skill-route…).</param>
public sealed record AttemptResult(
    string Scenario,
    int Attempt,
    bool Passed,
    string Outcome,
    string? Error,
    long DurationMs,
    int Turns,
    int ModelSteps,
    int ToolCalls,
    int ToolErrors,
    int NonZeroExits,
    int RepeatedToolCalls,
    IReadOnlyDictionary<string, int> ToolsByName,
    int Approvals,
    int DeniedApprovals,
    int Prompts,
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens,
    long ReasoningTokens,
    int RequestCount,
    decimal? Cost,
    IReadOnlyDictionary<string, int> Requests,
    int Compactions,
    IReadOnlyList<string> WorkspaceChanges,
    IReadOnlyList<CheckResult> Checks,
    string? FinalAnswer);

public sealed record CheckResult(string Name, bool Passed, string? Detail = null);
