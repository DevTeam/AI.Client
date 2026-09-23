namespace AI.Client.Application.Chat;

/// <summary>Effective limits used to prepare one request for a model.</summary>
public sealed record ChatContextLimits(
    long ContextWindowTokens,
    long ReservedOutputTokens,
    long ProtocolOverheadTokens,
    long SafetyMarginTokens);
