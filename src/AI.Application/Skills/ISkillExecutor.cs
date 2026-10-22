namespace AI.Application.Skills;

public interface ISkillExecutor
{
    string SkillId { get; }
    Task<SkillExecutionResult> RunAsync(SkillInvocation invocation, CancellationToken cancellationToken);
}
