namespace AI.Client.Mcp.BuiltIn.Files;

/// <summary>
/// Tracks an approximate character budget while a directory/search result is accumulated entry
/// by entry, as a second, size-based cap alongside each tool's entry-count limit.
///
/// A count limit alone assumes a "typical" entry: it was sized for ordinary path lengths, and
/// does not protect against a tree whose paths are all much longer than that, nor against the
/// tool's structured result being serialized twice (once into the MCP <c>CallToolResult</c>
/// content, then again as that whole result is stored as a chat message) — the incident this
/// guards against was a single `directory_tree` call whose 6,097 entries were within the entry
/// cap but produced 1.4MB of doubly-serialized JSON, large enough that the model's endpoint
/// rejected the request outright on the next turn. Budgeting on an approximate per-entry
/// contribution (not an exact serialized byte count) is deliberate: it only needs to keep the
/// result in a safe range, not predict its size precisely.
/// </summary>
internal sealed class ResultBudget(int limit)
{
    private int _used;

    /// <summary>
    /// Returns true and reserves <paramref name="characters"/> if the budget still has room for
    /// them; returns false, reserving nothing, once the limit would be exceeded. A caller that
    /// gets false should stop accumulating entries and report the result as truncated.
    /// </summary>
    public bool TryReserve(int characters)
    {
        if (_used + characters > limit) return false;
        _used += characters;
        return true;
    }
}
