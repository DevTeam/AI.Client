namespace AI.Web.Chats;

/// <summary>
/// Names the place a message from another chat came from — "Project / Chat / Branch" — and the
/// application link that opens it. Names are read from the Host and kept for a short while, so a
/// transcript with many messages from the same chat reads it once.
/// </summary>
public interface ISenderLocations
{
    /// <summary>The project, chat and branch names; null when the Host could not be reached or the chat is gone.</summary>
    Task<string?> GetNameAsync(Guid projectId, Guid chatId, Guid branchId, CancellationToken cancellationToken);

    /// <summary>The application link that opens the sender's branch.</summary>
    string GetLink(Guid projectId, Guid chatId, Guid branchId);
}
