namespace AI.Mcp.BuiltIn;

using System.Text.Encodings.Web;
using System.Text.Json;
using ModelContextProtocol.Protocol;

/// <summary>
/// Builds MCP results whose structured content always matches the declared output schema,
/// including failures: the Host validates every result against that schema.
/// </summary>
public sealed class BuiltInToolReply : IBuiltInToolReply
{
    // This text is read by a model, not embedded into HTML/JS — the default encoder's escaping
    // of every non-ASCII character (`\uXXXX`, 6 characters per character) is wasted cost here,
    // and a heavy one for anything but plain ASCII: a Cyrillic (or any other non-Latin) source
    // file read back through a tool inflates by roughly 6x on this pass alone, before the whole
    // CallToolResult is serialized a second time into the stored chat message (see
    // DefaultToolSessionFactory), which escapes it again on top of that.
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
