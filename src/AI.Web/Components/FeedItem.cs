using AI.Contracts.Chats;

namespace AI.Web.Components;

/// <summary>
/// One row in the transcript. Exactly one of <see cref="Message"/> / <see cref="ToolGroup"/>
/// is set: a plain message or a group of tool calls plus their result messages.
/// </summary>
public readonly record struct FeedItem(
    ChatMessageView? Message,
    IReadOnlyList<ChatMessageView>? ToolGroup);
