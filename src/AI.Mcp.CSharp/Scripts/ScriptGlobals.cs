namespace AI.Mcp.CSharp.Scripts;

using System.Text.Json;

/// <summary>
/// The object a script sees as <c>Args</c> and <c>Globals</c>. Roslyn scripting binds globals
/// statically, so this type is fixed rather than generated from the call's arguments: strings go
/// through <see cref="Args"/>, and anything structured through <see cref="Globals"/> as raw JSON
/// that the script reads with <see cref="JsonElement"/>.
/// </summary>
public sealed class ScriptGlobals
{
    public string[] Args { get; init; } = [];

    public IReadOnlyDictionary<string, JsonElement> Globals { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}
