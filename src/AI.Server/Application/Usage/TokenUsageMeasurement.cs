namespace AI.Application.Usage;

using AI.Contracts.Usage;

/// <summary>One finished request, as the transport saw it, before the scope says what it was for.</summary>
/// <param name="ReportedCost">The price the endpoint quoted, which wins over the connection's own prices.</param>
/// <param name="Shape">What the request started with, compared with the previous request of its branch.</param>
public sealed record TokenUsageMeasurement(
    string Model,
    Guid? ConnectionId,
    TokenCounts Tokens,
    bool Estimated,
    TimeSpan Duration,
    TimeSpan? FirstToken = null,
    decimal? ReportedCost = null,
    PromptShape? Shape = null);
