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

    public CallToolResult ReplyImage<T>(T value, ReadOnlyMemory<byte> data, string mediaType, bool isError = false)
    {
        var structured = JsonSerializer.SerializeToElement(value, Json);
        return new CallToolResult
        {
            StructuredContent = structured,
            // The text block stays: it is the structured result the Host validates, and the only
            // thing a non-multimodal consumer of this call can read. The image block is added
            // beside it, not instead of it.
            Content =
            [
                new TextContentBlock { Text = structured.GetRawText() },
                // FromBytes, not the property: on the wire 'data' is Base64 of the bytes, and the
                // block's Data member itself holds those Base64 characters (DecodedData holds the
                // bytes). Assigning the raw bytes to Data writes them out as a string that no
                // client can decode.
                ImageContentBlock.FromBytes(data, mediaType)
            ],
            IsError = isError
        };
    }
}
