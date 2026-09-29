namespace AI.Web.Skills;

using AI.Contracts.Skills;

/// <summary>One row of the composer's slash list: the skill and the characters of its name that matched.</summary>
/// <param name="Highlights">Indexes into <see cref="SkillDefinition.Name"/>, ascending.</param>
/// <param name="Alias">The alias the query matched, shown as "/alias" next to the name.</param>
public sealed record SkillCommandMatch(SkillDefinition Skill, IReadOnlyList<int> Highlights, string? Alias = null);
