namespace AI.Application.Chat;

using System.Text.Json;
using AI.Domain.Chats;

public sealed record ChatCompletionRequest(
    string BaseUrl,
    string Model,
    string? ApiKey,
    string Message,
    Guid? CredentialProfileId = null,
    IReadOnlyList<ChatCompletionMessage>? ContextMessages = null,
    IReadOnlyList<ChatToolDefinition>? Tools = null, ChatKind Kind = default, JsonElement? KindState = null,
    int KindStateVersion = 1, Guid? ProjectId = null);
