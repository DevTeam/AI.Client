namespace AI.Application.Tools;

/// <summary>Bounds on a single tool call the model streams, shared by the stream parser and argument validation.</summary>
public static class ToolCallLimits
{
    /// <summary>
    /// Characters of one call's JSON arguments. It sits above the largest content a tool accepts
    /// (<c>write_file</c> takes one MiB) with room for JSON escaping, so a call its schema allows is
    /// never refused only after the model has spent minutes generating it; it still stops a
    /// malformed stream from growing without bound.
    /// </summary>
    public const int ArgumentCharacters = 2 * 1024 * 1024;
}
