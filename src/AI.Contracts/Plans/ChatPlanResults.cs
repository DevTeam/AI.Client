namespace AI.Contracts.Plans;

using System.Text.Json;
using System.Text.Json.Serialization;
using Tools;

/// <summary>Reads the plan back from a stored plan tool result.</summary>
public static class ChatPlanResults
{
    /// <summary>The plan tool's name on the application server, without the server prefix.</summary>
    public const string ToolName = "plan_update";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static bool IsPlanCall(string callName) =>
        ToolRef.Parse(callName) is { IsApp: true, Name: ToolName };

    /// <summary>
    /// The plan a successful plan tool result carries. Null for an error, which left the previous
    /// plan as it was, and for anything that is not a plan result: stored history can be old or
    /// hand-edited, and a bad entry is skipped rather than taking the transcript down.
    /// </summary>
    public static ChatPlan? TryRead(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || root.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True
                || !root.TryGetProperty("structuredContent", out var structured)
                || structured.ValueKind != JsonValueKind.Object
                || !structured.TryGetProperty("plan", out var plan)
                || plan.ValueKind != JsonValueKind.Object) return null;
            var value = plan.Deserialize<ChatPlan>(Json);
            return value is { Steps: not null } && value.Steps.All(step => step is { Title: not null }) ? value : null;
        }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            return null;
        }
    }
}
