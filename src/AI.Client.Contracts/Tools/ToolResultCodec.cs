namespace AI.Client.Contracts.Tools;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Reads and writes the tool-result JSON persisted as a tool message's content.
/// </summary>
/// <remarks>
/// The stored shape is the MCP <c>CallToolResult</c> wire shape — <c>content</c>,
/// <c>structuredContent</c>, <c>isError</c>, <c>_meta</c> — which is exactly what earlier builds
/// already wrote by serializing the protocol object wholesale. Keeping that shape means every
/// existing chat reads back through this codec with no migration, and the result is stored once
/// rather than duplicated into parallel typed columns.
///
/// Nothing here trusts its input: a result can come from a third-party server, and a chat file can
/// be hand-edited. Anything unparseable degrades to a single text block instead of throwing, so a
/// malformed result cannot take the transcript down with it.
/// </remarks>
public sealed class ToolResultCodec(IToolResultModelProjector modelProjector) : IToolResultCodec
{
    private static readonly JsonSerializerOptions StorageJson = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Serializes the full result, metadata included, for durable history.</summary>
    public string Write(ToolCallResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var payload = new JsonObject { ["isError"] = result.IsError };
        var blocks = new JsonArray();
        foreach (var block in result.Content) blocks.Add(WriteBlock(block));
        if (blocks.Count > 0) payload["content"] = blocks;
        if (result.StructuredContent is { } structured) payload["structuredContent"] = JsonNode.Parse(structured.GetRawText());
        if (result.Meta is { } meta) payload["_meta"] = JsonNode.Parse(meta.GetRawText());
        return payload.ToJsonString(StorageJson);
    }

    /// <summary>
    /// Recovers a typed result from stored content. The returned <see cref="ToolCallResult.ModelContent"/>
    /// is reprojected rather than read back: a rehydrated result is for display, and re-deriving it
    /// keeps the "metadata never reaches the model" rule true even for history written by an older build.
    /// </summary>
    public ToolCallResult Read(string? storedContent)
    {
        return TryRead(storedContent) ?? Fallback(storedContent ?? string.Empty);
    }

    /// <summary>
    /// Recovers a result only when the stored content has a recognized tool-result shape. This lets
    /// context restoration preserve unknown legacy content verbatim instead of projecting it as a
    /// newly invented result envelope.
    /// </summary>
    public ToolCallResult? TryRead(string? storedContent)
    {
        if (string.IsNullOrWhiteSpace(storedContent)) return null;
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(storedContent);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Not JSON at all — an older plain-text tool message, or a hand-edited file.
            return null;
        }

        if (root.ValueKind != JsonValueKind.Object) return null;

        var hasErrorFlag = root.TryGetProperty("isError", out var errorFlag)
            && errorFlag.ValueKind is JsonValueKind.True or JsonValueKind.False;
        var isError = hasErrorFlag && errorFlag.GetBoolean();
        var hasStructured = root.TryGetProperty("structuredContent", out var structuredElement);
        var structured = hasStructured
            ? structuredElement.Clone()
            : (JsonElement?)null;
        var hasMeta = root.TryGetProperty("_meta", out var metaElement);
        var meta = hasMeta ? metaElement.Clone() : (JsonElement?)null;

        var blocks = new List<ToolContent>();
        var hasContent = root.TryGetProperty("content", out var contentElement)
            && contentElement.ValueKind == JsonValueKind.Array;
        if (hasContent)
            foreach (var block in contentElement.EnumerateArray())
                if (ReadBlock(block) is { } parsed) blocks.Add(parsed);

        // A host-side refusal is written as {"isError":true,"error":"..."} with no content array.
        var hasErrorMessage = root.TryGetProperty("error", out var message)
            && message.ValueKind == JsonValueKind.String;
        if (blocks.Count == 0 && hasErrorMessage)
            blocks.Add(ToolContent.OfText(message.GetString() ?? string.Empty));

        if (!hasContent && !hasStructured && !hasMeta && !hasErrorFlag && !hasErrorMessage) return null;

        return new ToolCallResult(blocks, structured, meta, isError,
            modelProjector.Project(blocks, structured, isError));
    }

    private ToolCallResult Fallback(string text)
    {
        var blocks = new[] { ToolContent.OfText(text) };
        return new ToolCallResult(blocks, null, null, false, modelProjector.Project(blocks, null, false));
    }

    private static JsonObject WriteBlock(ToolContent block)
    {
        var node = new JsonObject { ["type"] = block.Kind.ToWireType() };
        if (block.Text is not null) node["text"] = block.Text;
        if (block.MimeType is not null) node["mimeType"] = block.MimeType;
        if (block.Uri is not null) node["uri"] = block.Uri;
        if (block.Name is not null) node["name"] = block.Name;
        return node;
    }

    private static ToolContent? ReadBlock(JsonElement block)
    {
        if (block.ValueKind != JsonValueKind.Object) return null;
        var kind = ToolContentKindExtensions.FromWireType(Text(block, "type"));
        // An embedded resource nests its payload; lift the parts presentation reads.
        var resource = block.TryGetProperty("resource", out var nested) && nested.ValueKind == JsonValueKind.Object
            ? nested
            : block;
        return new ToolContent(kind, Text(resource, "text"), Text(resource, "mimeType"), Text(resource, "uri"), Text(block, "name"));
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
