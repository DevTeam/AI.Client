namespace AI.Application.Runs;

using Contracts.Chats;

/// <summary>What the model reads for a user message's text; see docs/34-asides-and-team-messages.md.</summary>
public interface IModelMessageHeader
{
    /// <summary>The model text of a message: its header, when it needs one, above <paramref name="content"/>.</summary>
    string Apply(ChatMessageView message, ChatDetails chat, string content);
}
