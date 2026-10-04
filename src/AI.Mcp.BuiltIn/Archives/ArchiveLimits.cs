namespace AI.Mcp.BuiltIn.Archives;

/// <summary>
/// Caps that keep one archive call bounded in size, time and — for extraction — in how much it can
/// write. They are deliberately separate from the file tool caps: an archive is a single path that
/// can describe thousands of entries and gigabytes of content.
/// </summary>
internal static class ArchiveLimits
{
    /// <summary>Entries returned by <see cref="ZipListTool"/> in one call.</summary>
    public const int Entries = 5000;

    /// <summary>Approximate characters of the listing result; see <see cref="Files.ResultBudget"/>.</summary>
    public const int ListCharacters = 131072;

    /// <summary>Characters of a single entry read by <see cref="ZipReadTool"/>.</summary>
    public const int ReadCharacters = 262144;

    /// <summary>Entries <see cref="ZipExtractTool"/> may write in one call.</summary>
    public const int ExtractEntries = 10000;

    /// <summary>Entries listed in an extraction or pack result before the list is cut short.</summary>
    public const int ResultEntries = 2000;

    /// <summary>
    /// Bytes <see cref="ZipExtractTool"/> may write and <see cref="ZipCreateTool"/> may read in one
    /// call. A refused call is safer than a silent partial one: an archive over this limit has to be
    /// narrowed with <c>pattern</c> rather than extracted in part.
    /// </summary>
    public const long TransferBytes = 268435456;

    /// <summary>Paths <see cref="ZipCreateTool"/> may be given in one call.</summary>
    public const int Sources = 1024;
}
