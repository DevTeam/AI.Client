using AI.Client.Contracts.Chat;

namespace AI.Client.Cli;

internal sealed record HeadlessSession(
    Guid Id,
    Guid ProjectId,
    string ProjectName,
    Guid EndpointProfileId,
    string EndpointName,
    string BaseUrl,
    string Model,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ChatCompletionMessage> Messages,
    int McpServerCount,
    int ToolPolicyCount,
    int DirectoryGrantCount);

internal sealed record HeadlessTurnResult(
    Guid SessionId,
    Guid TurnId,
    string Status,
    string? FinalText,
    int ChunkCount,
    long FirstTokenMs,
    long DurationMs,
    string CompletionReason,
    string? Error);
