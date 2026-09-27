namespace AI.Mcp.BuiltIn.Files;

using System.Collections.Frozen;

/// <summary>Result caps that keep a single tool call bounded in size and time.</summary>
internal static class FileLimits
{
    public const int ContentCharacters = 262144;
    public const int MultipleFilesPaths = 32;
    public const int MultipleFilesCharacters = 262144;
    public const int DirectoryEntries = 5000;
    public const int DirectoryCharacters = 262144;
    public const int TreeEntries = 20000;
    public const int TreeDepth = 32;
    public const int TreeCharacters = 131072;
    public const int SearchMatches = 1000;
    public const int SearchExamined = 200000;
    public const int SearchCharacters = 131072;
    public const int GrepMatches = 1000;
    public const int GrepFilesScanned = 5000;
    public const int GrepCharacters = 131072;
    public const int GrepLineCharacters = 400;
    public const int GrepFileBytes = 16777216;
    public const int BinaryProbeBytes = 8192;
    public const int Edits = 64;
    public const int WriteCharacters = 1048576;

    /// <summary>
    /// Directory names <see cref="DirectoryTreeTool"/>, <see cref="SearchFilesTool"/> and
    /// <see cref="GrepFilesTool"/> skip by
    /// default — version control metadata and build/dependency output that is almost never what
    /// an agent means by "the project", and that on a real repository can dwarf everything else
    /// combined (a `.git` folder alone routinely holds thousands of loose object files). Matched
    /// case-insensitively against the entry name, not the full path, so it applies at any depth.
    /// Each tool accepts `excludeDefaults: false` to see through this filter for one call.
    /// </summary>
    public static readonly FrozenSet<string> DefaultExcludedNames = new[]
    {
        ".git", ".hg", ".svn", "bin", "obj", "artifacts", "node_modules", ".vs", ".idea", ".vscode",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
}
