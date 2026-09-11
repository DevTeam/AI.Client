namespace AI.Client.Application.Tools;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

/// <summary>
/// A tool result split into the parts that have different audiences: <see cref="Content"/> and
/// <see cref="StructuredContent"/> drive presentation, <see cref="Meta"/> is host/UI metadata, and
/// <see cref="ModelContent"/> is the only part that goes back into the model's context.
/// </summary>
public sealed record AgentToolResult(
    IReadOnlyList<ToolContent> Content,
    JsonElement? StructuredContent,
    JsonElement? Meta,
    bool IsError,
    string ModelContent)
{
    // Result text is read back by a model, not embedded in a browser, so the default encoder's
    // `\uXXXX` escaping of every non-ASCII character is pure overhead here — the same reasoning
    // (and the same relaxed encoder) as ToolReply one layer in.
    private static readonly JsonSerializerOptions ModelFacingJson = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Renders the model-facing projection of a result: content, structured content and the error
    /// flag, but never <c>_meta</c>. Metadata is addressed to the host — a third-party server can
    /// put anything in it, including instructions aimed at the model — so it stays out of context.
    /// </summary>
    public static string ProjectForModel(
        IReadOnlyList<ToolContent> content,
        JsonElement? structuredContent,
        bool isError)
    {
        ArgumentNullException.ThrowIfNull(content);
        var payload = new JsonObject { ["isError"] = isError };
        var blocks = new JsonArray();
        foreach (var block in content) blocks.Add(block.ToModelJson());
        if (blocks.Count > 0) payload["content"] = blocks;
        if (structuredContent is { } structured) payload["structuredContent"] = JsonNode.Parse(structured.GetRawText());
        return payload.ToJsonString(ModelFacingJson);
    }

    /// <summary>A host-side failure that never reached the server, or a refusal by policy.</summary>
    public static AgentToolResult FromError(string message)
    {
        var content = new[] { ToolContent.OfText(message) };
        var payload = new JsonObject { ["isError"] = true, ["error"] = message };
        return new AgentToolResult(content, null, null, true, payload.ToJsonString(ModelFacingJson));
    }
}

/// <summary>One MCP content block, flattened to what presentation actually needs.</summary>
public sealed record ToolContent(
    ToolContentKind Kind,
    string? Text,
    string? MimeType,
    string? Uri,
    string? Name)
{
    public static ToolContent OfText(string text) => new(ToolContentKind.Text, text, null, null, null);

    internal JsonObject ToModelJson()
    {
        var node = new JsonObject { ["type"] = Kind.ToWireType() };
        if (Text is not null) node["text"] = Text;
        if (MimeType is not null) node["mimeType"] = MimeType;
        if (Uri is not null) node["uri"] = Uri;
        if (Name is not null) node["name"] = Name;
        return node;
    }
}

public enum ToolContentKind
{
    /// <summary>A block type this build does not model; kept so an unknown result still renders.</summary>
    Unknown,
    Text,
    Image,
    Audio,
    ResourceLink,
    Resource,
}

public static class ToolContentKindExtensions
{
    public static string ToWireType(this ToolContentKind kind) => kind switch
    {
        ToolContentKind.Text => "text",
        ToolContentKind.Image => "image",
        ToolContentKind.Audio => "audio",
        ToolContentKind.ResourceLink => "resource_link",
        ToolContentKind.Resource => "resource",
        _ => "unknown",
    };

    public static ToolContentKind FromWireType(string? type) => type switch
    {
        "text" => ToolContentKind.Text,
        "image" => ToolContentKind.Image,
        "audio" => ToolContentKind.Audio,
        "resource_link" => ToolContentKind.ResourceLink,
        "resource" => ToolContentKind.Resource,
        _ => ToolContentKind.Unknown,
    };
}
