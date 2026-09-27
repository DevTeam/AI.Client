namespace AI.Mcp.App;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

public sealed class AppToolReply : IAppToolReply
{
    public JsonSerializerOptions Json { get; } = Configure();

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

    public CallToolResult Reply<T, TMeta>(T value, TMeta meta, bool isError = false)
    {
        var result = Reply(value, isError);
        result.Meta = JsonSerializer.SerializeToNode(meta, Json)?.AsObject();
        return result;
    }
}
