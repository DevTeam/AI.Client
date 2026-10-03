namespace AI.Application.Skills;

using System.Text.Json;
using Json.Schema;
using Pure.DI;
using AI.Contracts.Skills;
using AI.Contracts.Usage;
using AI.Application.Usage;

/// <summary>Validates a skill's declared parameters before dispatching to its implementation.</summary>
public sealed class SkillRunner(ISkillCatalog catalog, [Tag("chat-rename")] ISkillExecutor chatRenameSkill,
    IGenericSkillExecutor? genericExecutor = null, [Tag("chat-reply-suggest")] ISkillExecutor? chatReplySuggestSkill = null,
    [Tag("skill-route")] ISkillExecutor? skillRouteSkill = null, [Tag("chat-tool-risk-assess")] ISkillExecutor? chatToolRiskAssessSkill = null,
    ITokenUsageMeter? usageMeter = null, [Tag("chat-comment-suggest")] ISkillExecutor? chatCommentSuggestSkill = null) : ISkillRunner
{
    private readonly object _gate = new();
    private readonly List<SkillRunRecord> _recent = [];
    private readonly Dictionary<string, ISkillExecutor> _executors =
        new ISkillExecutor?[] { chatRenameSkill, chatReplySuggestSkill, skillRouteSkill, chatToolRiskAssessSkill, chatCommentSuggestSkill }.OfType<ISkillExecutor>()
            .ToDictionary(executor => executor.SkillId, StringComparer.Ordinal);

    private TokenUsagePurpose Purpose(string skillId) =>
        skillId == chatRenameSkill.SkillId ? TokenUsagePurpose.Title
        : skillId == chatReplySuggestSkill?.SkillId ? TokenUsagePurpose.ReplySuggestion
        : skillId == chatCommentSuggestSkill?.SkillId ? TokenUsagePurpose.CommentSuggestion
        : skillId == skillRouteSkill?.SkillId ? TokenUsagePurpose.Routing
        : skillId == chatToolRiskAssessSkill?.SkillId ? TokenUsagePurpose.ToolRisk
        : TokenUsagePurpose.Skill;

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
        // Whatever the skill asks a model is attributed to it. Inside a run the scope adds only the
        // purpose, and the turn it serves is inherited; outside one it also names the chat.
        using var usageScope = usageMeter?.Begin(new TokenUsageScope(Purpose(invocation.SkillId), invocation.ProjectId,
            invocation.CurrentChatId));
        try
        {
            var skill = await catalog.GetByIdAsync(invocation.SkillId, invocation.ProjectId, cancellationToken);
            if (skill is not { Enabled: true })
                throw new ArgumentException($"Skill '{invocation.SkillId}' is unavailable.");
            invocation = invocation with { Parameters = Coerce(Normalize(invocation.Parameters), skill.ParametersSchema) };
            var evaluation = JsonSchema.Build(skill.ParametersSchema).Evaluate(invocation.Parameters,
                new EvaluationOptions { OutputFormat = OutputFormat.List });
            if (!evaluation.IsValid)
                throw new ArgumentException($"Parameters for skill '{skill.Id}' do not match its schema: {Describe(evaluation)}. "
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
    /// Models also quote scalar arguments, such as "1500" for an integer; a top-level string that
    /// parses as the type its property declares is taken as that value.
    /// </summary>
    private static JsonElement Coerce(JsonElement parameters, JsonElement schema)
    {
        if (parameters.ValueKind != JsonValueKind.Object || schema.ValueKind != JsonValueKind.Object
            || !schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
            return parameters;
        var changed = false;
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in parameters.EnumerateObject())
        {
            var value = property.Value;
            if (value.ValueKind == JsonValueKind.String
                && properties.TryGetProperty(property.Name, out var declared) && declared.ValueKind == JsonValueKind.Object
                && declared.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
                && Parse(value.GetString()!.Trim(), type.GetString()) is { } parsed)
            {
                value = parsed;
                changed = true;
            }
            result[property.Name] = value;
        }
        return changed ? JsonSerializer.SerializeToElement(result) : parameters;
    }

    private static JsonElement? Parse(string text, string? type) => type switch
    {
        "integer" when long.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign,
            System.Globalization.CultureInfo.InvariantCulture, out var integer) => JsonSerializer.SerializeToElement(integer),
        "number" when double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
            => JsonSerializer.SerializeToElement(number),
        "boolean" when bool.TryParse(text, out var flag) => JsonSerializer.SerializeToElement(flag),
        _ => null
    };

    /// <summary>Names each failing location, so a model can fix the one argument instead of resending the call.</summary>
    private static string Describe(EvaluationResults evaluation)
    {
        var errors = (evaluation.Details ?? [])
            .Prepend(evaluation)
            .Where(item => item.Errors is { Count: > 0 })
            .SelectMany(item => item.Errors!.Select(error =>
                $"{(item.InstanceLocation.ToString() is { Length: > 0 } location ? location : "/")} {error.Value}"))
            .Distinct()
            .ToArray();
        return errors.Length == 0 ? "invalid parameters" : string.Join("; ", errors);
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
            + "including the \"(Recommended)\" suffix, are examples to translate. Anything the instructions have you "
            + "show the user, such as a report, is part of your final answer; a closing line they ask for goes after "
            + "it and never replaces it. The user's follow-ups on the same task continue these instructions from the step "
            + "you reached; when the instructions name another skill for the next part of the work, run that skill.",
            invocation.CurrentChatId, Output: output);
    }
}
