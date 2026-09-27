namespace AI.Mcp.App;

using System.Text.Json;
using ModelContextProtocol.Protocol;

/// <summary>
/// Builds MCP results whose structured content always matches the declared output schema,
/// including failures: the Host validates every result against that schema.
/// </summary>
/// <remarks>
/// One serializer the server uses for arguments, results and schema generation alike, so an enum
/// is spelled the same way in the tool's schema and in the value it reads back. Relaxed escaping
/// keeps non-ASCII chat content from inflating roughly sixfold on its way to the model, exactly
/// as the built-in server does.
///
/// The instance exists so the serializer and the two factories share one configuration: a test
/// or an alternative transport can substitute a reply with a different <c>JsonSerializerOptions</c>
/// without rewiring every tool.
/// </remarks>
public interface IAppToolReply
{
    JsonSerializerOptions Json { get; }

    CallToolResult Reply<T>(T value, bool isError = false);

    /// <summary>
    /// A result with a second half addressed to the Host and the UI rather than to the model.
    /// <c>_meta</c> is excluded from the model-facing projection by contract, which is what lets a
    /// tool hand back something large for a person to read without spending the model's context on
    /// it.
    /// </summary>
    CallToolResult Reply<T, TMeta>(T value, TMeta meta, bool isError = false);
}
