namespace AI.Web.Components;

/// <summary>
/// Decides which of a running turn's texts the transcript shows under the turn row, and when it
/// may move on to the next one. A text that has just finished stays up long enough to be read;
/// the next one waits behind it instead of replacing it the moment it starts.
/// </summary>
public interface ITurnLiveText
{
    /// <param name="turnId">The running turn, or null when no turn is running.</param>
    /// <param name="candidate">The newest text of the turn: the step in flight, else its latest note.</param>
    /// <param name="held">The reader is on the shown text (pointer over it, a selection in it).</param>
    /// <param name="now">The current time.</param>
    LiveTextFrame Present(Guid? turnId, string? candidate, bool held, DateTimeOffset now);
}

/// <param name="Text">What to show, or null for nothing.</param>
/// <param name="Version">Changes when the shown text is replaced by a different one, not when it grows.</param>
/// <param name="NextCheck">When a waiting text becomes due; null when nothing is waiting on time.</param>
public readonly record struct LiveTextFrame(string? Text, int Version, DateTimeOffset? NextCheck);
