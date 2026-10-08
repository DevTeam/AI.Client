namespace AI.Mcp.BuiltIn.Files;

public sealed record TextFileResult(
    string Path,
    string Content,
    int FirstLine,
    int LineCount,
    bool Truncated,
    string? Error);

/// <summary>
/// The outcome of reading one image. <paramref name="Width"/> and <paramref name="Height"/> are the
/// pixel size read from the header, or <c>null</c> when the header could not be parsed;
/// <paramref name="Attached"/> says whether the image bytes themselves travel with this result as
/// an image content block, which is what lets a multimodal model look at the picture.
/// </summary>
public sealed record ImageFileResult(
    string Path,
    string MediaType,
    long Bytes,
    int? Width,
    int? Height,
    bool Attached,
    string? Error);

public sealed record FileText(string Path, string Content, bool Truncated, string? Error);

public sealed record MultipleFilesResult(FileText[] Files, string? Error);

public sealed record DirectoryEntryInfo(string Name, string Kind, long? Size, DateTimeOffset ModifiedAt);

public sealed record DirectoryListResult(string Path, DirectoryEntryInfo[] Entries, bool Truncated, string? Error);

public sealed record TreeEntry(string Path, string Kind, int Depth);

public sealed record DirectoryTreeResult(string Path, TreeEntry[] Entries, bool Truncated, string? Error);

public sealed record SearchFilesResult(string Path, string[] Matches, bool Truncated, string? Error);

/// <summary>
/// One matching line. <paramref name="Line"/> and <paramref name="Column"/> are 1-based, and
/// <paramref name="Before"/><paramref name="After"/> carry context lines only when the caller asked
/// for them; at the start or end of a file they are shorter rather than padded.
/// </summary>
public sealed record TextMatch(int Line, int Column, string Text, string[] Before, string[] After);

/// <summary>
/// The matches found in one file. <paramref name="MatchCount"/> counts every matching line even
/// when <paramref name="Matches"/> was cut short by a budget, and stays the full count when the
/// caller asked for <c>countOnly</c>.
/// </summary>
public sealed record TextFileMatches(string Path, int MatchCount, TextMatch[] Matches, bool Truncated);

public sealed record GrepFilesResult(
    string Path,
    string Query,
    TextFileMatches[] Files,
    int FilesScanned,
    int FilesSkipped,
    int TotalMatches,
    bool Truncated,
    string? Error);

public sealed record FileInfoResult(
    string Path,
    string Kind,
    long Size,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt,
    bool IsReadOnly,
    string? LinkTarget,
    string? Error);

public sealed record WriteFileResult(string Path, long Bytes, bool Created, string? Error);

public sealed record FileEdit(string OldText, string NewText);

public sealed record EditFileResult(string Path, int Applied, string Diff, bool DryRun, string? Error);

public sealed record CreateDirectoryResult(string Path, bool Created, string? Error);

public sealed record MoveFileResult(string Source, string Destination, string? Error);

/// <summary><paramref name="Bytes"/> is the size the file had just before it was removed.</summary>
public sealed record DeleteFileResult(string Path, bool Deleted, long Bytes, string? Error);

public sealed record DeleteDirectoryResult(string Path, bool Deleted, bool Recursive, string? Error);

public sealed record AllowedDirectory(string Root, bool Recursive, string[] Capabilities, string Purpose);

public sealed record AllowedDirectoriesResult(AllowedDirectory[] Directories);
