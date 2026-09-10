namespace AI.Client.Mcp.BuiltIn;

using System.Text.Json;
using ModelContextProtocol.Protocol;

/// <summary>
/// Builds MCP results whose structured content always matches the declared output schema,
/// including failures: the Host validates every result against that schema.
/// </summary>
internal static class ToolReply
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static CallToolResult Of<T>(T value, bool isError = false)
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
