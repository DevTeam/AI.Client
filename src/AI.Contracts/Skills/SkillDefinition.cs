namespace AI.Contracts.Skills;

using System.Text.Json;

/// <summary>A skill available to this Host. Content is the exact SKILL.md shown to the user.</summary>
/// <param name="Kind">
/// <see cref="SkillKinds.Generic"/> runs in an isolated model call without tools and returns JSON;
/// <see cref="SkillKinds.Playbook"/> hands its instructions to the calling model, which follows them
/// with its ordinary permission-checked tools; <see cref="SkillKinds.Executor"/> is bundled code.
/// </param>
/// <param name="Aliases">Short extra commands, such as "compact", that find the skill in the slash list and in search.</param>
public sealed record SkillDefinition(string Id, string Name, string Description, string Source,
    string Content, bool Enabled, JsonElement ParametersSchema,
    JsonElement? ResultSchema = null, IReadOnlyList<string>? AllowedTools = null,
    Guid? ProjectId = null, long Revision = 0, string Kind = SkillKinds.Generic,
    IReadOnlyList<string>? Aliases = null);

public static class SkillKinds
{
    public const string Generic = "generic";
    public const string Playbook = "playbook";
    public const string Executor = "executor";
}
