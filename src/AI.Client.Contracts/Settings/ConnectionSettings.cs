namespace AI.Client.Contracts.Settings;

/// <summary>
/// One endpoint the application can talk to. Beyond how to reach it, a connection carries what the
/// person running it thinks of it: whether delegated work should land here, and how it compares to
/// the others on capability and price. Those judgements are opinions rather than facts, so they are
/// deliberately coarse, and absent until someone states one.
/// </summary>
/// <param name="ForSubtasks">
/// Where a subtask may go when neither the task nor the call named a connection. Any number of
/// connections can hold it, and unaddressed work is spread over them in turn — several providers
/// share a fan-out that one would have had to answer alone. A disabled connection never holds it.
/// </param>
/// <param name="Capability">How much this endpoint can be trusted with, 1 to 5; null when nobody has said.</param>
/// <param name="Cost">What it costs to use, 1 to 5; null when nobody has said.</param>
/// <param name="GoodFor">
/// One line of what the two ratings cannot express — a long context, vision, being local and
/// offline. Read by the model when it chooses, so it is prose on purpose.
/// </param>
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
    int? Cost = null,
    string? GoodFor = null,
    long? ContextWindowTokens = null,
    long? ReservedOutputTokens = null);
