namespace AI.Application.Skills;

using AI.Contracts.Skills;

public interface ISkillRunner
{
    Task<SkillRunRecord> RunAsync(SkillInvocation invocation, CancellationToken cancellationToken);
    IReadOnlyList<SkillRunRecord> ListRecent();
}
