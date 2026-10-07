namespace AI.Domain.Chats;

using Common;

/// <summary>A stable, open identifier for the behavior of a chat.</summary>
public readonly record struct ChatKind
{
    public static readonly ChatKind Conversation = new("conversation");
    public static readonly ChatKind Guide = new("guide");
    public static readonly ChatKind Demo = new("demo");
    public static readonly ChatKind TeamDemo = new("team-demo");
    public static readonly ChatKind Scheduled = new("scheduled");

    public ChatKind(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character =>
                !char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '-'))
            throw new DomainException("Chat kind must be a lowercase identifier.");
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}
