namespace AI.Application.Skills;

using Json.Schema;
using AI.Contracts.Skills;

/// <summary>Validates a skill's declared parameters before dispatching to its implementation.</summary>
public sealed class SkillRunner(ISkillCatalog catalog, ChatTitleSkill chatTitleSkill,
    GenericSkillExecutor? genericExecutor = null) : ISkillRunner
{
    private readonly object _gate = new();
    private readonly List<SkillRunRecord> _recent = [];
    private readonly Dictionary<string, ISkillExecutor> _executors =
        new Dictionary<string, ISkillExecutor>(StringComparer.Ordinal)
        {
            [chatTitleSkill.SkillId] = chatTitleSkill
        };

    public IReadOnlyList<SkillRunRecord> ListRecent()
    {
        lock (_gate) return _recent.ToArray();
    }

    public async Task<SkillRunRecord> RunAsync(SkillInvocation invocation, CancellationToken cancellationToken)
    {
        var started = new SkillRunRecord(Guid.CreateVersion7(), invocation.SkillId, invocation.ProjectId,
            null, "Running", "Skill started.", DateTimeOffset.UtcNow, null);
        lock (_gate)
        {
            _recent.Insert(0, started);
            if (_recent.Count > 50) _recent.RemoveAt(_recent.Count - 1);
        }

        SkillExecutionResult result;
        try
        {
            var skill = await catalog.GetByIdAsync(invocation.SkillId, invocation.ProjectId, cancellationToken);
            if (skill is not { Enabled: true })
                throw new ArgumentException($"Skill '{invocation.SkillId}' is unavailable.");
            if (!JsonSchema.Build(skill.ParametersSchema).Evaluate(invocation.Parameters).IsValid)
                throw new ArgumentException($"Parameters for skill '{skill.Id}' do not match its schema.");
            result = _executors.TryGetValue(skill.Id, out var executor)
                ? await executor.RunAsync(invocation, cancellationToken)
                : await (genericExecutor ?? throw new InvalidOperationException("The generic skill executor is unavailable."))
                    .RunAsync(skill, invocation, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = new SkillExecutionResult("Cancelled", "Skill was cancelled.");
        }
        catch (Exception error)
        {
            result = new SkillExecutionResult("Failed", error.Message);
        }
        var completed = started with
        {
            ChatId = result.ChatId,
            Status = result.Status,
            Message = result.Message,
            Title = result.Title,
            Output = result.Output,
            CompletedAt = DateTimeOffset.UtcNow
        };
        lock (_gate)
        {
            var index = _recent.FindIndex(item => item.Id == started.Id);
            if (index >= 0) _recent[index] = completed;
        }
        return completed;
    }
}
