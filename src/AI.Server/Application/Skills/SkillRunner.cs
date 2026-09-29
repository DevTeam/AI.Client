namespace AI.Application.Skills;

using System.Text.Json;
using Json.Schema;
using AI.Contracts.Skills;

/// <summary>Validates a skill's declared parameters before dispatching to its implementation.</summary>
public sealed class SkillRunner(ISkillCatalog catalog, ChatRenameSkill chatRenameSkill,
    GenericSkillExecutor? genericExecutor = null) : ISkillRunner
{
    private readonly object _gate = new();
    private readonly List<SkillRunRecord> _recent = [];
    private readonly Dictionary<string, ISkillExecutor> _executors =
        new Dictionary<string, ISkillExecutor>(StringComparer.Ordinal)
        {
            [chatRenameSkill.SkillId] = chatRenameSkill
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
            invocation = invocation with { Parameters = Normalize(invocation.Parameters) };
            if (!JsonSchema.Build(skill.ParametersSchema).Evaluate(invocation.Parameters).IsValid)
                throw new ArgumentException($"Parameters for skill '{skill.Id}' do not match its schema. "
                    + $"Pass an object matching: {skill.ParametersSchema.GetRawText()}");
            result = skill.Kind switch
            {
                SkillKinds.Playbook => Playbook(skill, invocation),
                SkillKinds.Executor => _executors.TryGetValue(skill.Id, out var executor)
                    ? await executor.RunAsync(invocation, cancellationToken)
                    : throw new InvalidOperationException($"Skill '{skill.Id}' has no executor in this Host."),
                _ => await (genericExecutor ?? throw new InvalidOperationException("The generic skill executor is unavailable."))
                    .RunAsync(skill, invocation, cancellationToken)
            };
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

    /// <summary>
    /// Models routinely leave out the arguments of a skill whose parameters are all optional, or
    /// send the object as a JSON string. Both mean the same thing as the object itself.
    /// </summary>
    private static JsonElement Normalize(JsonElement parameters)
    {
        if (parameters.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return JsonSerializer.SerializeToElement(new { });
        if (parameters.ValueKind != JsonValueKind.String) return parameters;
        var text = parameters.GetString();
        if (string.IsNullOrWhiteSpace(text)) return JsonSerializer.SerializeToElement(new { });
        try { return JsonSerializer.Deserialize<JsonElement>(text); }
        catch (JsonException) { return parameters; }
    }

    /// <summary>
    /// A playbook runs in the caller's own turn: the runner only validates the arguments and hands
    /// back the instructions with the ids the caller cannot otherwise see. Every step the
    /// instructions describe goes through the caller's ordinary permission-checked tools.
    /// </summary>
    private static SkillExecutionResult Playbook(SkillDefinition skill, SkillInvocation invocation)
    {
        var output = JsonSerializer.SerializeToElement(new
        {
            kind = SkillKinds.Playbook,
            instructions = SkillMarkdown.Body(skill.Content),
            arguments = invocation.Parameters,
            tools = skill.AllowedTools ?? [],
            context = new
            {
                projectId = invocation.ProjectId,
                chatId = invocation.CurrentChatId,
                branchId = invocation.CurrentBranchId
            }
        });
        return new SkillExecutionResult("Completed",
            "Loaded the playbook. Follow output.instructions now, in this turn, with your ordinary tools; "
            + "output.context holds the current project, chat and branch ids, so do not look them up. Write every "
            + "question, option label, title and answer in the user's language: quoted labels in the instructions, "
            + "including the \"(Recommended)\" suffix, are examples to translate.",
            invocation.CurrentChatId, Output: output);
    }
}
