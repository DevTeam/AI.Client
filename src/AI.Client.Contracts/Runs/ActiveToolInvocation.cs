namespace AI.Client.Contracts.Runs;

/// <summary>
/// A tool call the Host is executing right now.
/// </summary>
/// <param name="Progress">
/// How far the server says it has got, from its <c>notifications/progress</c> messages. Null when
/// the server reports nothing, which is the common case — a spinner, not a bar.
/// </param>
/// <remarks>
/// Carries identity and raw arguments rather than a rendered label: the row is described by the
/// same adapters that describe it afterwards in history, so a call does not change its wording the
/// moment it finishes.
///
/// The run snapshot holds a list even though calls currently execute one at a time, so that
/// running them in parallel later is not blocked by the contract.
/// </remarks>
public sealed record ActiveToolInvocation(
    string CallId,
    string Name,
    string Arguments,
    DateTimeOffset StartedAt,
    double? Progress = null,
    double? Total = null,
    string? Message = null);
