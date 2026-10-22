namespace AI.Application.Skills;

using System.Text.Json;

/// <summary>The project is the trusted execution scope; parameters select a target within it.</summary>
public sealed record SkillInvocation(string SkillId, Guid ProjectId, JsonElement Parameters, Guid? CurrentChatId = null,
    Guid? CurrentBranchId = null);
