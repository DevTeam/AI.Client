namespace AI.Application.Chat;

public sealed record ChatToolDefinition(string Name, string Description, System.Text.Json.JsonElement InputSchema);
