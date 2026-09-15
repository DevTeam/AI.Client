namespace AI.Client.Mcp.App;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

/// <summary>
/// Builds MCP results whose structured content always matches the declared output schema,
/// including failures: the Host validates every result against that schema.
/// </summary>
internal static class ToolReply
{
    /// <summary>
    /// The one serializer the server uses for arguments, results and schema generation alike, so
    /// an enum is spelled the same way in the tool's schema and in the value it reads back.
    /// Relaxed escaping keeps non-ASCII chat content from inflating roughly sixfold on its way to
    /// the model, exactly as the built-in server does.
    /// </summary>
    public static readonly JsonSerializerOptions Json = Configure();

    private static JsonSerializerOptions Configure()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        options.Converters.Add(new JsonStringEnumConverter());
        // Frozen so the one shared instance cannot be reconfigured from under a call in flight;
        // the resolver is populated here rather than left to the first serialization to pick.
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

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
