namespace AI.Application.Chat;

using AI.Contracts.Chat;

/// <param name="Content">
/// What is persisted and shown. For a tool message this is the full result, metadata included.
/// </param>
/// <param name="ModelContent">
/// What the provider is sent in place of <paramref name="Content"/>, when the two differ. A tool
/// result's host/UI metadata is addressed to this application, not to the model — a third-party
/// server can put anything in it — so the model gets a projection without it. Null means the two
/// are the same, which is the case for every message a person or the model itself wrote.
/// </param>
public sealed record ChatCompletionMessage(string Role, string Content,
    IReadOnlyList<ChatToolCall>? ToolCalls = null, string? ToolCallId = null, string? ModelContent = null)
{
    /// <summary>The text to put on the wire for this message.</summary>
    public string ForModel => ModelContent ?? Content;
}
