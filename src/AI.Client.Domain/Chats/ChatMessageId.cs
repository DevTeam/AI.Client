using AI.Client.Domain.Common;

namespace AI.Client.Domain.Chats;

public readonly record struct ChatMessageId
{
    public ChatMessageId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("Chat message ID cannot be empty.");
        }

        Value = value;
    }

    public Guid Value { get; }
}
