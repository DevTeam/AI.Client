namespace AI.Web.Skills;

using AI.Contracts.Skills;

public interface ISkillApi
{
    Task<IReadOnlyList<SkillDefinition>> ListAsync(CancellationToken cancellationToken);
}
