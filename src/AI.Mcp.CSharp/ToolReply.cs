namespace AI.Mcp.CSharp;

using System.Text.Encodings.Web;
using System.Text.Json;
using ModelContextProtocol.Protocol;

/// <summary>
/// Builds MCP results whose structured content always matches the declared output schema,
/// including failures: the Host validates every result against that schema.
/// </summary>
public sealed class ToolReply : IToolReply
{
    // This text is read by a model, not embedded into HTML/JS — escaping every non-ASCII character
    // (`\uXXXX`, 6 characters per character) would inflate a script result carrying any non-Latin
    // text roughly sixfold on this pass alone, before the whole CallToolResult is serialized a
    // second time into the stored chat message.
    public JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public CallToolResult Reply<T>(T value, bool isError = false)
    {
        var structured = JsonSerializer.SerializeToElement(value, Json);
        return new CallToolResult
        {
            StructuredContent = structured,
            Content = [new TextContentBlock { Text = structured.GetRawText() }],
            IsError = isError
        };
    }
}
