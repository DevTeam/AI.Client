namespace AI.Application.Skills;

using AI.Contracts.Skills;

public interface ISkillCatalog
{
    IReadOnlyList<SkillDefinition> List();
    SkillDefinition? GetById(string id);
}
