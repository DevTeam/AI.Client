namespace AI.Application.Skills;

using Json.Schema;

/// <summary>Validates a skill's declared parameters before dispatching to its implementation.</summary>
public sealed class SkillRunner(ISkillCatalog catalog, ChatTitleSkill chatTitleSkill) : ISkillRunner
{
    private readonly Dictionary<string, ISkillExecutor> _executors =
        new Dictionary<string, ISkillExecutor>(StringComparer.Ordinal)
        {
            [chatTitleSkill.SkillId] = chatTitleSkill
        };

    public Task RunAsync(SkillInvocation invocation, CancellationToken cancellationToken)
    {
        var skill = catalog.GetById(invocation.SkillId);
        if (skill is not { Enabled: true })
            throw new ArgumentException($"Skill '{invocation.SkillId}' is unavailable.", nameof(invocation));
        if (!_executors.TryGetValue(skill.Id, out var executor))
            throw new InvalidOperationException($"Skill '{skill.Id}' has no executor.");
        if (!JsonSchema.Build(skill.ParametersSchema).Evaluate(invocation.Parameters).IsValid)
            throw new ArgumentException($"Parameters for skill '{skill.Id}' do not match its schema.", nameof(invocation));
        return executor.RunAsync(invocation, cancellationToken);
    }
}
