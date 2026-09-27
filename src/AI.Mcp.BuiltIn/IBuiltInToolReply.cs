namespace AI.Mcp.BuiltIn;

using System.Text.Json;
using ModelContextProtocol.Protocol;

/// <summary>
/// Builds MCP results whose structured content always matches the declared output schema,
/// including failures: the Host validates every result against that schema.
/// </summary>
public interface IBuiltInToolReply
{
    JsonSerializerOptions Json { get; }

    CallToolResult Reply<T>(T value, bool isError = false);
}
