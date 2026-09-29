namespace AI.Web.Skills;

using AI.Contracts.Skills;

public interface ISkillApi
{
    Task<IReadOnlyList<SkillDefinition>> ListAsync(Guid? projectId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SkillRunRecord>> ListRunsAsync(CancellationToken cancellationToken);
    Task<SkillDefinition> SaveAsync(SkillWriteRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(SkillDefinition skill, CancellationToken cancellationToken);
}
