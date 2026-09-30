namespace AI.Contracts.Runs;

/// <summary>
/// A draft of the user's next message, written for the answer <paramref name="LeafMessageId"/>. It
/// belongs to that answer only: once the branch moves on it is stale and the Host no longer serves it.
/// </summary>
public sealed record ChatReplySuggestion(Guid LeafMessageId, string Text);
