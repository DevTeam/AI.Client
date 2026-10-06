namespace AI.Web.Components;

/// <summary>
/// Decides which of a running turn's texts the transcript shows under the turn row, and when it
/// may move on to the next one. A text that has just finished stays up long enough to be read;
/// the next one waits behind it instead of replacing it the moment it starts. The texts it has
/// moved past are kept, newest last, so the transcript can show the latest few as a stack.
/// </summary>
public interface ITurnLiveText
{
    /// <param name="turnId">The running turn, or null when no turn is running.</param>
    /// <param name="texts">
    /// The turn's texts, oldest first: its notes, then the step in flight. The last one is the
    /// candidate; the earlier ones fill the stack when the turn is first presented, as when the
    /// chat is opened mid-run.
    /// </param>
    /// <param name="held">The reader is on the shown texts (pointer over them, a selection in them).</param>
    /// <param name="now">The current time.</param>
    LiveTextFrame Present(Guid? turnId, IReadOnlyList<string> texts, bool held, DateTimeOffset now);
}

/// <param name="Id">Stays with the text while it grows; a different text gets a new one.</param>
/// <param name="Text">The text as shown.</param>
public readonly record struct LiveNote(int Id, string Text);

/// <param name="Notes">The texts shown so far in this turn, oldest first; the last is the current one.</param>
/// <param name="NextCheck">When a waiting text becomes due; null when nothing is waiting on time.</param>
public readonly record struct LiveTextFrame(IReadOnlyList<LiveNote> Notes, DateTimeOffset? NextCheck)
{
    /// <summary>The current text, or null for nothing.</summary>
    public string? Text => Notes.Count > 0 ? Notes[^1].Text : null;

    /// <summary>Changes when the current text is replaced by a different one, not when it grows.</summary>
    public int Version => Notes.Count > 0 ? Notes[^1].Id : 0;
}
