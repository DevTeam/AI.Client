namespace AI.Application.Skills;

using AI.Contracts.Skills;
using Search;

/// <summary>Ranks skill metadata without sending the complete catalog to a model.</summary>
public sealed class SkillCandidateSelector : ISkillCandidateSelector
{
    public IReadOnlyList<SkillDefinition> FindCandidates(string query, IReadOnlyList<SkillDefinition> skills, int limit,
        string? activeSkillId = null)
    {
        var name = query.Trim().TrimStart('/');
        var terms = SearchText.Terms(query);
        // Every entry a term reaches is a candidate, best first: a threshold that grows with the
        // query's length dropped the one skill a short query described in other words.
        return skills.Select(skill => new { Skill = skill, Score = Score(skill, name, terms) })
            .Where(item => item.Score > 0 || item.Skill.Id == activeSkillId)
            .OrderByDescending(item => item.Skill.Id == activeSkillId)
            .ThenByDescending(item => item.Score)
            .ThenBy(item => item.Skill.Id, StringComparer.Ordinal)
            .Take(Math.Max(0, limit))
            .Select(item => item.Skill)
            .ToArray();
    }

    private static int Score(SkillDefinition skill, string name, IReadOnlyList<IReadOnlySet<string>> terms)
    {
        if (name.Length == 0) return 0;
        if (skill.Id.Equals(name, StringComparison.OrdinalIgnoreCase)) return 1_000;
        if (skill.Aliases?.Any(alias => alias.Equals(name, StringComparison.OrdinalIgnoreCase)) == true) return 900;
        var names = SearchText.Index([skill.Id, skill.Name, .. skill.Aliases ?? []]);
        var description = SearchText.Index([skill.Description]);
        return terms.Sum(term => SearchText.Matches(term, names) ? 12
            : SearchText.BeginsAWordOf(term, skill.Id) ? 8
            : SearchText.Matches(term, description) ? 4
            : 0);
    }
}
