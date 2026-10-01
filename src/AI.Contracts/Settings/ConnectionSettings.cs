namespace AI.Contracts.Settings;

/// <summary>
/// One endpoint the application can talk to. Beyond how to reach it, a connection carries what the
/// person running it thinks of it: whether delegated work should land here, and how it compares to
/// the others on capability. That judgement is an opinion rather than a fact, so it is
/// deliberately coarse, and absent until someone states one.
/// </summary>
/// <param name="ForSubtasks">
/// Where a subtask may go when neither the task nor the call named a connection. Any number of
/// connections can hold it, and unaddressed work is spread over them in turn — several providers
/// share a fan-out that one would have had to answer alone. A disabled connection never holds it.
/// </param>
/// <param name="Capability">How much this endpoint can be trusted with, 1 to 5; null when nobody has said.</param>
/// <param name="GoodFor">
/// One line of what the capability rating cannot express — a long context, vision, being local and
/// offline. Read by the model when it chooses, so it is prose on purpose.
/// </param>
/// <param name="Prices">
/// What the endpoint charges per million tokens, used to price usage it does not price itself;
/// null leaves such usage unpriced rather than guessing.
/// </param>
/// <summary>
/// One model the endpoint advertises through <c>GET /v1/models</c>. The resolver normalises the
/// upstream OpenAI-compatible payload into this shape so the UI does not need to know the
/// provider-specific schema differences.
/// </summary>
/// <param name="Id">The model identifier as it must be sent back in <c>model</c>.</param>
/// <param name="DisplayName">
/// A human-readable label, if the provider supplied one distinct from <paramref name="Id"/>; the UI
/// prefers it when present.
/// </param>
/// <param name="OwnedBy">The provider's own owner tag, kept for sorting and debugging only.</param>
public sealed record ResolvedModelInfo(string Id, string? DisplayName = null, string? OwnedBy = null);

/// <summary>
/// What the connection editor currently shows, sent when it asks for the model catalog. The form is
/// usually ahead of what is saved (a new connection is not saved at all), so the Host must not read
/// the endpoint from its settings.
/// </summary>
/// <param name="BaseUrl">The Base URL as typed.</param>
/// <param name="ApiKey">A key typed but not yet saved; null falls back to the saved credential.</param>
public sealed record ResolveConnectionModelsRequest(string BaseUrl, string? ApiKey = null);

public sealed record ConnectionSettings(
    Guid Id,
    string Name,
    string BaseUrl,
    string Model,
    bool Enabled,
    bool IsDefault,
    bool HasCredential,
    bool ForSubtasks = false,
    int? Capability = null,
    string? GoodFor = null,
    long? ContextWindowTokens = null,
    long? ReservedOutputTokens = null,
    Usage.TokenPrices? Prices = null);
