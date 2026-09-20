namespace AI.Client.Contracts.Settings;

public enum ContextLimitSource { Default, Override }

public sealed record ResolvedConnectionContextLimits(
    long ContextWindowTokens,
    ContextLimitSource ContextWindowSource,
    long ReservedOutputTokens,
    ContextLimitSource ReservedOutputSource);
