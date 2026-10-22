namespace AI.Application.Skills;

using AI.Contracts.Skills;

public interface ISkillCandidateSelector
{
    IReadOnlyList<SkillDefinition> FindCandidates(string query, IReadOnlyList<SkillDefinition> skills, int limit,
        string? activeSkillId = null);
}
