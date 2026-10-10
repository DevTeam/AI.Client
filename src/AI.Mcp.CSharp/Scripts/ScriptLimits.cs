namespace AI.Mcp.CSharp.Scripts;

/// <summary>Caps that keep one script run bounded in size and time.</summary>
internal static class ScriptLimits
{
    public const int CodeCharacters = 262144;
    public const int OutputCharacters = 32768;
    public const int Arguments = 256;
    public const int Imports = 64;
    public const int References = 64;
    public const int Globals = 64;
    public const int Variables = 64;
    public const int Diagnostics = 64;
    public const int ValueCharacters = 4096;
    public const int DefaultTimeoutMs = 600000;
    public const int MaxTimeoutMs = 86400000;

    /// <summary>
    /// Namespaces every script gets without asking. Deliberately small: they are the ones a script
    /// is nearly always written with, and each one added here is a name a caller cannot shadow by
    /// mistake.
    /// </summary>
    public static readonly string[] DefaultImports =
    [
        "System",
        "System.Collections",
        "System.Collections.Generic",
        "System.Globalization",
        "System.IO",
        "System.Linq",
        "System.Text",
        "System.Text.Json",
        "System.Text.RegularExpressions",
        "System.Threading",
        "System.Threading.Tasks"
    ];
}
