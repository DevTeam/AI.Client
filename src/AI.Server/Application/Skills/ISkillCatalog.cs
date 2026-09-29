namespace AI.Application.Skills;

using AI.Contracts.Skills;

public interface ISkillCatalog
{
    IReadOnlyList<SkillDefinition> List();
    SkillDefinition? GetById(string id);
    Task<IReadOnlyList<SkillDefinition>> ListAsync(Guid? projectId, CancellationToken cancellationToken);
    Task<SkillDefinition?> GetByIdAsync(string id, Guid? projectId, CancellationToken cancellationToken);
    Task<SkillWriteResult> SaveAsync(SkillWriteRequest request, CancellationToken cancellationToken);
    Task<SkillWriteResult> DeleteAsync(string id, string scope, Guid? projectId, long revision,
        CancellationToken cancellationToken);
    Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken);
}
