namespace AI.Client.Contracts.Chat;

public sealed record ChatCompletionChunk(string Content, string? Model = null);
