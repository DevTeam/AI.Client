namespace AI.Application.Chats;

/// <summary>
/// Serializes work on one chat, so a read-modify-write cannot interleave with another one on the
/// same chat while leaving different chats independent.
/// </summary>
public interface IChatSynchronization
{
    /// <summary>Holds the chat's lease until the returned value is disposed.</summary>
    Task<IDisposable> EnterAsync(Guid chatId, CancellationToken cancellationToken);
}
