namespace AI.Mcp.BuiltIn.Files;

using System.Collections.Frozen;

/// <summary>Result caps that keep a single tool call bounded in size and time.</summary>
internal static class FileLimits
{
    /// <summary>
    /// Characters one <c>read_text_file</c> call returns, whole or ranged, ending after a whole line.
    /// Whatever is read stays in the model's context for the rest of the run and is sent again with
    /// every later step, so a large file is read in parts, or its parts are handed to subtasks,
    /// instead of being taken in at once.
    /// </summary>
    public const int ReadCharacters = 51200;

    /// <summary>Lines a read without a range returns: enough to decide where to look further.</summary>
    public const int WholeReadLines = 2000;

    /// <summary>
    /// Lines an <c>edit_file</c> diff keeps from the start and from the end of each side of a long
    /// replacement. The caller sent that text itself, so the result only needs to show where it went.
    /// </summary>
    public const int DiffSideLines = 12;

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
    /// Bytes of one image <see cref="ReadImageFileTool"/> is willing to hand to the model. It
    /// matches the Host's own asset limit (<c>ResourceAssetService.MaximumBytes</c>): a larger file
    /// would be rejected by the store that has to keep it, after the whole call had been paid for.
    /// </summary>
    public const int ImageBytes = 15 * 1024 * 1024;

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
