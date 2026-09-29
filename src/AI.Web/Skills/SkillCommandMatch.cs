namespace AI.Web.Skills;

using AI.Contracts.Skills;

/// <summary>One row of the composer's slash list: the skill and the characters of its title that matched.</summary>
/// <param name="Highlights">Indexes into <see cref="Title"/>, ascending.</param>
/// <param name="Alias">The best alias the query matched; the row then leads with "/alias" and names the skill after it.</param>
public sealed record SkillCommandMatch(SkillDefinition Skill, IReadOnlyList<int> Highlights, string? Alias = null)
{
    /// <summary>The text the row leads with: the matched alias, otherwise the skill name.</summary>
    public string Title => Alias ?? Skill.Name;
}
