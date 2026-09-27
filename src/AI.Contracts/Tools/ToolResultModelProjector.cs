namespace AI.Contracts.Tools;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

/// <summary>
/// Produces the model-facing output for a tool result. Successful structured output takes
/// precedence over content blocks; errors keep their content because it carries the actionable
/// failure description. Host/UI metadata never participates in the projection.
/// </summary>
public sealed class ToolResultModelProjector : IToolResultModelProjector
{
    private static readonly JsonSerializerOptions ModelFacingJson = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Project(
        IReadOnlyList<ToolContent> content,
        JsonElement? structuredContent,
        bool isError)
    {
        ArgumentNullException.ThrowIfNull(content);

        // Match the OpenAI Agents MCP adapter: prefer structured output for successful calls,
        // but keep content for errors because that is where MCP tools report corrective detail.
        if (!isError && structuredContent is { } structured && HasValue(structured))
            return JsonNode.Parse(structured.GetRawText())!.ToJsonString(ModelFacingJson);

        var blocks = new JsonArray();
        foreach (var block in content) blocks.Add(block.ToModelJson());
        return blocks.Count == 1
            ? blocks[0]!.ToJsonString(ModelFacingJson)
            : blocks.ToJsonString(ModelFacingJson);
    }

    private static bool HasValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => false,
        JsonValueKind.Object => value.EnumerateObject().Any(),
        JsonValueKind.Array => value.GetArrayLength() > 0,
        JsonValueKind.String => !string.IsNullOrEmpty(value.GetString()),
        _ => true
    };
}
