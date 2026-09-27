namespace AI.Contracts.Tools;

/// <summary>
/// Identifies the tool behind one invocation, using only what survives into chat history.
/// </summary>
/// <remarks>
/// Presentation must render identically during a run and after a restart, so it is built from the
/// persisted call name rather than from a live session descriptor. <paramref name="Title"/> and
/// <paramref name="Hints"/> are filled in only when a session happens to be open; adapters treat
/// them as extras and never require them.
/// </remarks>
public sealed record ToolRef(
    string CallName,
    string ServerPrefix,
    string Name,
    string? Title = null,
    ToolSafety Hints = ToolSafety.Unknown)
{
    /// <summary>Prefix the Host puts in front of every built-in tool's provider-facing name.</summary>
    public const string BuiltInPrefix = "mcp_built_in__";

    /// <summary>Prefix for the Host's own server, whose tools read and change the application's data.</summary>
    public const string AppPrefix = "mcp_app__";

    /// <summary>
    /// Splits a persisted call name into server prefix and tool name. A name with no recognizable
    /// prefix is taken whole, which is what makes an unknown third-party tool still presentable.
    /// </summary>
    public static ToolRef Parse(string? callName)
    {
        var name = callName ?? string.Empty;
        var separator = name.IndexOf("__", StringComparison.Ordinal);
        return separator < 0
            ? new ToolRef(name, string.Empty, name)
            : new ToolRef(name, name[..(separator + 2)], name[(separator + 2)..]);
    }

    public bool IsBuiltIn => ServerPrefix == BuiltInPrefix;

    public bool IsApp => ServerPrefix == AppPrefix;

    /// <summary>
    /// A last-resort human label for a tool nothing knows anything about: <c>read_text_file</c>
    /// becomes "Read text file". Never invented beyond reformatting the name the server gave.
    /// </summary>
    public string FallbackLabel
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title)) return Title.Trim();
            var words = Name.Replace('_', ' ').Replace('-', ' ').Trim();
            if (words.Length == 0) return "Tool";
            return char.ToUpperInvariant(words[0]) + words[1..];
        }
    }
}
