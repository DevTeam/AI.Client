namespace AI.Application.Tools;

using System.Text.Json;
using Chat;
using Contracts.Settings;

public sealed class RunCompletionProtocol : IRunCompletionProtocol
{
    public RunCompletionProtocol()
    {
        var schema = JsonSerializer.Deserialize<JsonElement>(Schema);
        var definition = new ChatToolDefinition(Name,
            "Finish the run. Use complete when the request is satisfied, or blocked when you cannot proceed or "
            + "answer reliably. Explain the result in finalAnswer.",
            schema);
        Tool = new AgentTool(definition,
            ToolDescriptor.Basic(Name, OriginalName, definition.Description, schema),
            AppMcpServer.Id, OriginalName, "app-finish-run-v1");
    }

    public AgentTool Tool { get; }

    public RunCompletionDecision Parse(string arguments)
    {
        using var outer = JsonDocument.Parse(arguments);
        // Some endpoints send the arguments object encoded once more as a JSON string.
        using var inner = outer.RootElement.ValueKind == JsonValueKind.String
            ? JsonDocument.Parse(outer.RootElement.GetString() ?? string.Empty) : null;
        var root = (inner ?? outer).RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new ArgumentException("Completion arguments must be an object.");
        var statusText = RequiredString(root, "status").ToLowerInvariant();
        var status = statusText switch
        {
            "complete" => RunCompletionStatus.Complete,
            "blocked" => RunCompletionStatus.Blocked,
            _ => throw new ArgumentException("Completion status must be complete or blocked. Call an ordinary tool to continue working.")
        };
        bool? includePrevious = root.TryGetProperty("includePreviousText", out var include)
            ? include.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null }
            : null;
        var answer = OptionalString(root, "finalAnswer")
            ?? (includePrevious is true ? string.Empty
                : throw new ArgumentException("A completion decision requires a user-facing finalAnswer."));
        return new RunCompletionDecision(status, answer, includePrevious);
    }

    public string RejectResult(string reason) => JsonSerializer.Serialize(new
    {
        accepted = false,
        error = reason,
        instruction = "Correct the decision and call app_finish_run again."
    });

    private static string RequiredString(JsonElement root, string name) =>
        OptionalString(root, name) ?? throw new ArgumentException($"Completion argument '{name}' is required.");

    private static string? OptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? NullIfWhiteSpace(property.GetString())
            : null;

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public const string Name = "app_finish_run";
    private const string OriginalName = "finish_run";
    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "status": { "type": "string", "enum": ["complete", "blocked"] },
            "finalAnswer": { "type": "string", "description": "The user-facing answer in Markdown, shown after your previous unpublished text unless includePreviousText is false. Never refer to text the user will not see. For blocked, explain the limitation and any partial result." },
            "includePreviousText": { "type": "boolean", "description": "Your previous unpublished plain-text message, if any, is published ahead of finalAnswer unless this is false. Pass false only to discard it, such as a note about work you did not do." }
          },
          "required": ["status", "finalAnswer"],
          "additionalProperties": false
        }
        """;
}
