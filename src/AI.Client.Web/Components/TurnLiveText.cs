namespace AI.Client.Web.Components;

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

    private Guid? _turnId;
    private string? _shown;
    private DateTimeOffset _grewAt;
    private int _version;
    // Texts this turn has already moved past. A step the protocol rejects is drafted and then
    // dropped, which leaves the earlier note as the newest candidate again; going back to it would
    // read as the model repeating itself.
    private readonly HashSet<string> _retired = new(StringComparer.Ordinal);

    public LiveTextFrame Present(Guid? turnId, string? candidate, bool held, DateTimeOffset now)
    {
        if (turnId is null)
        {
            _turnId = null;
            _shown = null;
            _retired.Clear();
            return new LiveTextFrame(null, _version, null);
        }

        if (turnId != _turnId)
        {
            _turnId = turnId;
            _shown = null;
            _retired.Clear();
        }

        var next = candidate?.Trim();
        if (string.IsNullOrEmpty(next)) return new LiveTextFrame(_shown, _version, null);

        if (_shown is null)
        {
            Show(next, now);
            return new LiveTextFrame(_shown, _version, null);
        }

        // The same text growing, or the step in flight landing as the note it was drafting.
        if (next.StartsWith(_shown, StringComparison.Ordinal))
        {
            if (next.Length > _shown.Length)
            {
                _shown = next;
                _grewAt = now;
            }
            return new LiveTextFrame(_shown, _version, null);
        }

        // A shorter copy of what is already up: a publication that lags behind the one shown.
        if (_shown.StartsWith(next, StringComparison.Ordinal) || _retired.Contains(next))
            return new LiveTextFrame(_shown, _version, null);

        var due = _grewAt + ReadingTime(_shown);
        if (held) return new LiveTextFrame(_shown, _version, null);
        if (now < due) return new LiveTextFrame(_shown, _version, due);

        Show(next, now);
        return new LiveTextFrame(_shown, _version, null);
    }

    private void Show(string text, DateTimeOffset now)
    {
        if (_shown is not null) _retired.Add(_shown);
        _shown = text;
        _grewAt = now;
        _version++;
    }

    private static TimeSpan ReadingTime(string text)
    {
        var reading = TimeSpan.FromSeconds(text.Length / CharactersPerSecond);
        return reading < MinReadingTime ? MinReadingTime : reading > MaxReadingTime ? MaxReadingTime : reading;
    }
}
