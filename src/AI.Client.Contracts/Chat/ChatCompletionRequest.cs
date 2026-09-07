namespace AI.Client.Contracts.Chat;

public sealed record ChatCompletionRequest(
    string BaseUrl,
    string Model,
    string? ApiKey,
    string Message,
    Guid? CredentialProfileId = null,
    IReadOnlyList<ChatCompletionMessage>? ContextMessages = null,
    IReadOnlyList<ChatToolDefinition>? Tools = null);
