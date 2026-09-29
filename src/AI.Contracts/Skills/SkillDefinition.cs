namespace AI.Contracts.Skills;

using System.Text.Json;

/// <summary>A skill available to this Host. Content is the exact SKILL.md shown to the user.</summary>
public sealed record SkillDefinition(string Id, string Name, string Description, string Source,
    string Content, bool Enabled, JsonElement ParametersSchema,
    JsonElement? ResultSchema = null, IReadOnlyList<string>? AllowedTools = null,
    Guid? ProjectId = null, long Revision = 0);
