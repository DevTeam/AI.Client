namespace AI.Client.Mcp.BuiltIn.Files;

/// <summary>Result caps that keep a single tool call bounded in size and time.</summary>
internal static class FileLimits
{
    public const int ContentCharacters = 262144;
    public const int MultipleFilesPaths = 32;
    public const int MultipleFilesCharacters = 262144;
    public const int DirectoryEntries = 5000;
    public const int TreeEntries = 20000;
    public const int TreeDepth = 32;
    public const int SearchMatches = 1000;
    public const int SearchExamined = 200000;
    public const int Edits = 64;
    public const int WriteCharacters = 1048576;
}
