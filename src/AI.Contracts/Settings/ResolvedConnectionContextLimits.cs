namespace AI.Contracts.Settings;

/// <summary>Where an effective context limit came from.</summary>
/// <remarks>
/// <see cref="Detected"/> is what the endpoint's model catalog reported for the connection's
/// model; <see cref="Default"/> is the conservative fallback used when nothing else is known.
/// </remarks>
public enum ContextLimitSource { Default, Override, Detected }

public sealed record ResolvedConnectionContextLimits(
    long ContextWindowTokens,
    ContextLimitSource ContextWindowSource,
    long ReservedOutputTokens,
    ContextLimitSource ReservedOutputSource);
