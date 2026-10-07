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

    /// <summary>
    /// Adds an image block to the result, so the bytes reach a multimodal model instead of only the
    /// metadata describing them. <see cref="Reply{T}"/> cannot do this: it emits text alone, and an
    /// image that is only described in text is not an image the model can look at.
    /// </summary>
    /// <param name="value">Structured result, serialized as the text block and as the structured content.</param>
    /// <param name="data">The image bytes themselves.</param>
    /// <param name="mediaType">Media type of <paramref name="data"/>, like <c>image/png</c>.</param>
    /// <param name="isError">Whether the result reports a failure.</param>
    CallToolResult ReplyImage<T>(T value, ReadOnlyMemory<byte> data, string mediaType, bool isError = false);
}
