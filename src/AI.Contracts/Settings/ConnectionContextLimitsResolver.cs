namespace AI.Contracts.Settings;

/// <summary>Resolves nullable connection overrides against safe OpenAI-compatible defaults.</summary>
public sealed class ConnectionContextLimitsResolver : IConnectionContextLimitsResolver
{
    public ResolvedConnectionContextLimits Resolve(ConnectionSettings? connection) => new(
        connection?.ContextWindowTokens ?? 32_768,
        connection?.ContextWindowTokens is null ? ContextLimitSource.Default : ContextLimitSource.Override,
        connection?.ReservedOutputTokens ?? 4_096,
        connection?.ReservedOutputTokens is null ? ContextLimitSource.Default : ContextLimitSource.Override);
}
