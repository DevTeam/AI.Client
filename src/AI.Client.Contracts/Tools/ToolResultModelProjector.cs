namespace AI.Client.Contracts.Tools;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

/// <summary>
/// Produces the canonical model-facing JSON for a tool result: content, structured content and
/// the error flag, but never host/UI metadata.
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
        var payload = new JsonObject { ["isError"] = isError };
        var blocks = new JsonArray();
        foreach (var block in content) blocks.Add(block.ToModelJson());
        if (blocks.Count > 0) payload["content"] = blocks;
        if (structuredContent is { } structured)
            payload["structuredContent"] = JsonNode.Parse(structured.GetRawText());
        return payload.ToJsonString(ModelFacingJson);
    }
}
