namespace AI.Client.Contracts.Chat;

public sealed record ChatToolCall(string Id, string Name, string Arguments);
public sealed record ChatToolDefinition(string Name, string Description, System.Text.Json.JsonElement InputSchema);
