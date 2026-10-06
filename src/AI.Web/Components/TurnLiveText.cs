namespace AI.Web.Components;

/// <remarks>
/// One instance per transcript: it remembers what was shown and since when. The reading time is
/// counted from the moment the shown text last grew, so a note that finished before a long tool
/// call has usually been read by the time the next one starts, and the switch costs no wait.
/// </remarks>
public sealed class TurnLiveText : ITurnLiveText
{
    // Roughly how fast a reader gets through prose; the bounds keep a one-liner from flashing by
    // and a long note from holding the turn back.
    private const double CharactersPerSecond = 20;
    private static readonly TimeSpan MinReadingTime = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan MaxReadingTime = TimeSpan.FromSeconds(6);

    // Enough for the largest stack the settings allow plus the note leaving it.
    private const int KeptNotes = 8;

    private Guid? _turnId;
    private readonly List<LiveNote> _notes = [];
    private DateTimeOffset _grewAt;
    private int _nextId;
    // Texts this turn has already moved past. A step the protocol rejects is drafted and then
    // dropped, which leaves the earlier note as the newest candidate again; going back to it would
    // read as the model repeating itself.
    private readonly HashSet<string> _retired = new(StringComparer.Ordinal);

    public LiveTextFrame Present(Guid? turnId, IReadOnlyList<string> texts, bool held, DateTimeOffset now)
    {
        if (turnId is null)
        {
            _turnId = null;
            Forget();
            return Frame(null);
        }

        if (turnId != _turnId)
        {
            _turnId = turnId;
            Forget();
        }

        var next = texts.Count > 0 ? texts[^1]?.Trim() : null;
        if (string.IsNullOrEmpty(next)) return Frame(null);

        if (_notes.Count == 0)
        {
            Seed(texts, next, now);
            Show(next, now);
            return Frame(null);
        }

        var shown = _notes[^1].Text;
        // The same text growing, or the step in flight landing as the note it was drafting.
        if (next.StartsWith(shown, StringComparison.Ordinal))
        {
            if (next.Length > shown.Length)
            {
                _notes[^1] = _notes[^1] with { Text = next };
                _grewAt = now;
            }
            return Frame(null);
        }

        // A shorter copy of what is already up: a publication that lags behind the one shown.
        if (shown.StartsWith(next, StringComparison.Ordinal) || _retired.Contains(next))
            return Frame(null);

        var due = _grewAt + ReadingTime(shown);
        if (held) return Frame(null);
        if (now < due) return Frame(due);

        Show(next, now);
        return Frame(null);
    }

    /// <summary>
    /// A turn presented for the first time may already have notes behind it (a chat opened
    /// mid-run): they fill the stack as read, so the newest is shown at once and nothing waits.
    /// </summary>
    private void Seed(IReadOnlyList<string> texts, string next, DateTimeOffset now)
    {
        var earlier = texts.Take(texts.Count - 1)
            .Select(text => text?.Trim())
            .OfType<string>()
            .Where(text => text.Length > 0 && !next.StartsWith(text, StringComparison.Ordinal))
            .TakeLast(KeptNotes - 1);
        foreach (var text in earlier)
        {
            if (_notes.Count > 0 && _notes[^1].Text == text) continue;
            Show(text, now);
        }
    }

    private void Forget()
    {
        _notes.Clear();
        _retired.Clear();
    }

    private void Show(string text, DateTimeOffset now)
    {
        if (_notes.Count > 0) _retired.Add(_notes[^1].Text);
        _notes.Add(new LiveNote(++_nextId, text));
        if (_notes.Count > KeptNotes) _notes.RemoveAt(0);
        _grewAt = now;
    }

    private LiveTextFrame Frame(DateTimeOffset? nextCheck) => new(_notes.ToArray(), nextCheck);

    private static TimeSpan ReadingTime(string text)
    {
        var reading = TimeSpan.FromSeconds(text.Length / CharactersPerSecond);
        return reading < MinReadingTime ? MinReadingTime : reading > MaxReadingTime ? MaxReadingTime : reading;
    }
}
