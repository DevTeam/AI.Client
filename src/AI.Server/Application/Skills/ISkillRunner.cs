namespace AI.Application.Skills;

public interface ISkillRunner
{
    Task RunAsync(SkillInvocation invocation, CancellationToken cancellationToken);
}
