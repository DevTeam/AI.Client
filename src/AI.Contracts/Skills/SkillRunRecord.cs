namespace AI.Contracts.Skills;

public sealed record SkillRunRecord(Guid Id, string SkillId, Guid ProjectId, Guid? ChatId,
    string Status, string Message, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
    string? Title = null, System.Text.Json.JsonElement? Output = null);
