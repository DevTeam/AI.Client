namespace AI.Client.Mcp.BuiltIn.Files;

public sealed record TextFileResult(
    string Path,
    string Content,
    int FirstLine,
    int LineCount,
    bool Truncated,
    string? Error);

public sealed record FileText(string Path, string Content, bool Truncated, string? Error);

public sealed record MultipleFilesResult(FileText[] Files, string? Error);

public sealed record DirectoryEntryInfo(string Name, string Kind, long? Size, DateTimeOffset ModifiedAt);

public sealed record DirectoryListResult(string Path, DirectoryEntryInfo[] Entries, bool Truncated, string? Error);

public sealed record TreeEntry(string Path, string Kind, int Depth);

public sealed record DirectoryTreeResult(string Path, TreeEntry[] Entries, bool Truncated, string? Error);

public sealed record SearchFilesResult(string Path, string[] Matches, bool Truncated, string? Error);

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

public sealed record AllowedDirectory(string Root, bool Recursive, string[] Capabilities);

public sealed record AllowedDirectoriesResult(AllowedDirectory[] Directories);
