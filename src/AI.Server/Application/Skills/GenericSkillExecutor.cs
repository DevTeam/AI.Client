namespace AI.Application.Skills;

using System.Text;
using System.Text.Json;
using AI.Application.Chat;
using AI.Application.Projects;
using AI.Application.Settings;
using AI.Contracts.Chat;
using AI.Contracts.Skills;
using Json.Schema;

/// <summary>Runs a declarative skill on caller-supplied data; it has no application tools.</summary>
public sealed class GenericSkillExecutor(IProjectService projects, IGlobalSettingsRepository settings,
    IGlobalSecretStore secrets, IChatCompletionClient completion) : IGenericSkillExecutor
{
    public async Task<SkillExecutionResult> RunAsync(SkillDefinition skill, SkillInvocation invocation,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            var token = deadline.Token;
            var project = await projects.GetAsync(invocation.ProjectId, token);
            if (project is null) return new("Failed", "Project not found.");
            var global = await settings.LoadAsync(token);
            var connection = global.Connections.FirstOrDefault(item => item.Id == project.ConnectionId && item.Enabled)
                ?? global.Connections.FirstOrDefault(item => item.IsDefault && item.Enabled)
                ?? global.Connections.FirstOrDefault(item => item.Enabled);
            if (connection is null) return new("Failed", "No enabled model connection is available.");
            var messages = new List<ChatCompletionMessage>
            {
                new("system", skill.Content + "\nReturn only JSON matching the declared result schema. Do not include Markdown fences."),
                new("user", $"Run {skill.Id} with arguments: {invocation.Parameters.GetRawText()}")
            };
            var request = new ChatCompletionRequest(connection.BaseUrl, connection.Model,
                await secrets.GetAsync("connection", connection.Id, token), skill.Name, connection.Id, messages, []);
            var content = new StringBuilder();
            await foreach (var chunk in completion.StreamAsync(request, token))
            {
                if (chunk.ToolCalls is { Count: > 0 })
                    return new("Failed", "A declarative skill cannot call application tools.");
                content.Append(chunk.Content);
            }
            JsonElement output;
            try { output = JsonSerializer.Deserialize<JsonElement>(content.ToString()); }
            catch (JsonException) { return new("Failed", "The model returned invalid JSON."); }
            if (skill.ResultSchema is { } schema && !JsonSchema.Build(schema).Evaluate(output).IsValid)
                return new("Failed", "The model result does not match the skill result schema.");
            return new("Completed", "Skill returned a result.", Output: output);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new("Cancelled", "Skill was cancelled.");
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return new("Failed", "Skill timed out.");
        }
    }
}
