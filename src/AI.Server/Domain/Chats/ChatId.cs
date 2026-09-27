namespace AI.Domain.Chats;

using Common;

public readonly record struct ChatId
{
    public ChatId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException("Chat ID cannot be empty.");
        }

        Value = value;
    }

    public Guid Value { get; }
}
