namespace AI.Application.Chats;

/// <summary>
/// Makes sort keys for the manual order of pinned chats. Keys compare ordinally, and a new one
/// can always be made between two neighbours, so moving a chat rewrites that one chat and never
/// its siblings.
/// </summary>
public interface IPinOrderKeys
{
    /// <summary>Returns a key strictly after <paramref name="lower"/> and strictly before <paramref name="upper"/>;
    /// null stands for the open start and end of the list.</summary>
    string Between(string? lower, string? upper);
}
