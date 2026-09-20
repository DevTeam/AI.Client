namespace AI.Client.Application.Tools;

using System.Text.Json;
using Contracts.Chat;
using Contracts.Settings;

public sealed class RunCompletionProtocol : IRunCompletionProtocol
{
    public RunCompletionProtocol()
    {
        var schema = JsonSerializer.Deserialize<JsonElement>(Schema);
        var definition = new ChatToolDefinition(Name,
            "Report whether the user's request is complete. This is the only way to publish a final answer after using tools. "
            + "Use continue when work remains, complete only when the definition of done is satisfied, and blocked only when progress requires user input or an external change.",
            schema);
        Tool = new AgentTool(definition,
            ToolDescriptor.Basic(Name, OriginalName, definition.Description, schema),
            AppMcpServer.Id, OriginalName, "app-finish-run-v1");
    }

    public AgentTool Tool { get; }

    public RunCompletionDecision Parse(string arguments)
    {
        using var document = JsonDocument.Parse(arguments);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new ArgumentException("Completion arguments must be an object.");
        var statusText = RequiredString(root, "status");
        var status = statusText switch
        {
            "complete" => RunCompletionStatus.Complete,
            "continue" => RunCompletionStatus.Continue,
            "blocked" => RunCompletionStatus.Blocked,
            _ => throw new ArgumentException("Completion status must be complete, continue, or blocked.")
        };
        var decision = new RunCompletionDecision(status, OptionalString(root, "finalAnswer"),
            Strings(root, "completed"), Strings(root, "evidence"), Strings(root, "remaining"),
            OptionalString(root, "nextAction"));
        Validate(decision);
        return decision;
    }

    public string ContinueResult(RunCompletionDecision decision) => JsonSerializer.Serialize(new
    {
        accepted = true,
        status = "continue",
        remaining = decision.Remaining,
        nextAction = decision.NextAction,
        instruction = "Continue the work. Call app_finish_run again after the next meaningful step."
    });

    public string RejectResult(string reason) => JsonSerializer.Serialize(new
    {
        accepted = false,
        error = reason,
        instruction = "Correct the decision and call app_finish_run again."
    });

    private static void Validate(RunCompletionDecision decision)
    {
        if (decision.Status == RunCompletionStatus.Complete)
        {
            if (string.IsNullOrWhiteSpace(decision.FinalAnswer))
                throw new ArgumentException("A complete result requires finalAnswer.");
            if (decision.Completed.Count == 0)
                throw new ArgumentException("A complete result requires at least one completed item.");
            if (decision.Remaining.Count > 0)
                throw new ArgumentException("A complete result cannot contain remaining work.");
        }
        else if (decision.Status == RunCompletionStatus.Continue)
        {
            if (decision.Remaining.Count == 0)
                throw new ArgumentException("A continue result requires remaining work.");
            if (string.IsNullOrWhiteSpace(decision.NextAction))
                throw new ArgumentException("A continue result requires nextAction.");
        }
        else if (string.IsNullOrWhiteSpace(decision.FinalAnswer))
        {
            throw new ArgumentException("A blocked result requires a user-facing finalAnswer.");
        }
    }

    private static string RequiredString(JsonElement root, string name) =>
        OptionalString(root, name) ?? throw new ArgumentException($"Completion argument '{name}' is required.");

    private static string? OptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? NullIfWhiteSpace(property.GetString())
            : null;

    private static string[] Strings(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property)) return [];
        if (property.ValueKind != JsonValueKind.Array)
            throw new ArgumentException($"Completion argument '{name}' must be an array.");
        return property.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String
                ? NullIfWhiteSpace(item.GetString())
                : throw new ArgumentException($"Completion argument '{name}' must contain strings."))
            .Where(item => item is not null).Select(item => item!).ToArray();
    }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public const string Name = "app_finish_run";
    private const string OriginalName = "finish_run";
    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "status": { "type": "string", "enum": ["complete", "continue", "blocked"] },
            "finalAnswer": { "type": "string", "description": "User-facing answer. Required for complete and blocked; omitted for continue." },
            "completed": { "type": "array", "items": { "type": "string" } },
            "evidence": { "type": "array", "items": { "type": "string" } },
            "remaining": { "type": "array", "items": { "type": "string" } },
            "nextAction": { "type": "string", "description": "The next concrete action. Required for continue." }
          },
          "required": ["status", "completed", "evidence", "remaining"],
          "additionalProperties": false
        }
        """;
}
