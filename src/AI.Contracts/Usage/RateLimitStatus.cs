namespace AI.Contracts.Usage;

/// <summary>One of the provider's limits as its last response stated it.</summary>
/// <param name="Limit">How many the window allows; null when the provider did not say.</param>
/// <param name="Remaining">How many are left in the window.</param>
/// <param name="ResetsAt">When the window refills; null when the provider did not say.</param>
public sealed record RateLimitWindow(long? Limit, long? Remaining, DateTimeOffset? ResetsAt);

/// <summary>
/// The limits a connection's endpoint reported with its last response. Absent for endpoints that
/// report none, which is most self-hosted ones.
/// </summary>
public sealed record RateLimitStatus(DateTimeOffset ObservedAt, RateLimitWindow? Requests, RateLimitWindow? Tokens);
