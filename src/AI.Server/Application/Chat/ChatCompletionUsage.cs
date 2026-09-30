namespace AI.Application.Chat;

using AI.Contracts.Usage;

/// <summary>
/// What the endpoint itself reported a request used. Only a report counts: an estimate is made
/// further up, where it can be labelled as one.
/// </summary>
/// <param name="Cost">The price the endpoint quoted for the request, when it quotes one (OpenRouter does).</param>
public sealed record ChatCompletionUsage(TokenCounts Tokens, decimal? Cost = null);
