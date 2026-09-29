namespace AI.Contracts.Skills;

public sealed record SkillWriteRequest(string Scope, Guid? ProjectId, string Content,
    long Revision = 0, bool Enabled = true);

public sealed record SkillWriteResult(string Status, SkillDefinition? Skill = null, string? Error = null);
