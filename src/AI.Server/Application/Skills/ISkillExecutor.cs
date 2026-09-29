namespace AI.Application.Skills;

public interface ISkillExecutor
{
    string SkillId { get; }
    Task RunAsync(SkillInvocation invocation, CancellationToken cancellationToken);
}
