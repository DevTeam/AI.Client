namespace AI.Contracts.Settings;

/// <summary>
/// Resolves nullable connection overrides against what the model catalog reported and, failing
/// both, against safe OpenAI-compatible defaults.
/// </summary>
public sealed class ConnectionContextLimitsResolver : IConnectionContextLimitsResolver
{
    /// <summary>
    /// The window assumed when neither the person nor the catalog says. Small on purpose: a local
    /// model served with a short context must keep working, and a larger guess would overflow it.
    /// </summary>
    public const long DefaultContextWindowTokens = 32_768;

    public const long DefaultReservedOutputTokens = 4_096;

    public ResolvedConnectionContextLimits Resolve(ConnectionSettings? connection)
    {
        var (window, windowSource) = connection switch
        {
            { ContextWindowTokens: { } tokens } => (tokens, ContextLimitSource.Override),
            // A detection belongs to the model it was made for; after a model change it says nothing.
            { DetectedContextWindow: { } detected } when string.Equals(detected.Model, connection.Model, StringComparison.Ordinal)
                && detected.Tokens > DefaultReservedOutputTokens => (detected.Tokens, ContextLimitSource.Detected),
            _ => (DefaultContextWindowTokens, ContextLimitSource.Default)
        };
        return new(window, windowSource,
            connection?.ReservedOutputTokens ?? DefaultReservedOutputTokens,
            connection?.ReservedOutputTokens is null ? ContextLimitSource.Default : ContextLimitSource.Override);
    }
}
