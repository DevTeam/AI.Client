namespace AI.Application.Skills;

using AI.Contracts.Skills;

public interface IGenericSkillExecutor
{
    Task<SkillExecutionResult> RunAsync(SkillDefinition skill, SkillInvocation invocation,
        CancellationToken cancellationToken);
}
