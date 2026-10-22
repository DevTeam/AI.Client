namespace AI.Domain.Chats;

/// <summary>
/// How a user message reached its branch. Only <see cref="Turn"/> starts a turn: an aside stands
/// between turns without an answer, and an in-turn message belongs to the turn it joined.
/// </summary>
public enum ChatMessageDelivery
{
    Turn,

    /// <summary>Appended without starting a turn; no model has answered it.</summary>
    Aside,

    /// <summary>Taken by a running turn at a step boundary and read by the model in that turn.</summary>
    InTurn
}
