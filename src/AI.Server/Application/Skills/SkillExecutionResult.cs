namespace AI.Application.Skills;

public sealed record SkillExecutionResult(string Status, string Message, Guid? ChatId = null, string? Title = null,
    System.Text.Json.JsonElement? Output = null);
