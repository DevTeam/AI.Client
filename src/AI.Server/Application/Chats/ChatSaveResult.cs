namespace AI.Application.Chats;

public sealed record ChatSaveResult(bool IsSaved, long Revision)
{
    public static ChatSaveResult Saved(long revision) => new(true, revision);
    public static ChatSaveResult Conflict(long revision) => new(false, revision);
}
