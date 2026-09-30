namespace AI.Web.State;

/// <summary>
/// What the empty chat's tips know about this person: whether they hid the tips, and which
/// features they have already used, so a tip for something familiar steps aside for the rest.
/// Kept in this browser's localStorage only.
/// </summary>
public interface IChatTipsState
{
    bool IsHidden { get; }

    bool IsUsed(string tipId);

    /// <summary>Raised after anything above changes.</summary>
    event Action? Changed;

    /// <summary>Loads the saved state. Safe to call more than once; only the first call reads.</summary>
    Task InitializeAsync();

    Task SetHiddenAsync(bool hidden);

    /// <summary>Records that the feature behind a tip was used. A repeat costs nothing.</summary>
    Task MarkUsedAsync(string tipId);
}
