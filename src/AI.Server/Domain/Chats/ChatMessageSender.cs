namespace AI.Domain.Chats;

/// <summary>
/// The run that submitted a message on a model's behalf: the chat and branch it ran in, filled in
/// by the server from the tool run context, and what the message is meant to be.
/// </summary>
public sealed record ChatMessageSender(Guid ChatId, Guid BranchId, string? Intent = null)
{
    public static readonly IReadOnlyList<string> Intents = ["question", "answer", "decision", "status", "blocker", "done"];
}
