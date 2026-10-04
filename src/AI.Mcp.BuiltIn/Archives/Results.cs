namespace AI.Mcp.BuiltIn.Archives;

/// <summary>
/// One entry as the archive stores it. <paramref name="Path"/> keeps the stored name, normalized to
/// forward slashes; <paramref name="Size"/> is the uncompressed length and stays 0 for a directory.
/// </summary>
public sealed record ArchiveEntryInfo(string Path, string Kind, long Size, long CompressedSize, DateTimeOffset ModifiedAt);

/// <summary>
/// The listing of an archive. <paramref name="EntryCount"/>, <paramref name="TotalBytes"/> and
/// <paramref name="CompressedBytes"/> describe the whole archive even when a <c>pattern</c> or a
/// cap left fewer rows in <paramref name="Entries"/>.
/// </summary>
public sealed record ZipListResult(
    string Path,
    ArchiveEntryInfo[] Entries,
    int EntryCount,
    long TotalBytes,
    long CompressedBytes,
    bool Truncated,
    string? Error);

public sealed record ZipReadResult(string Path, string EntryPath, string Content, long Size, bool Truncated, string? Error);

/// <summary>One file an extraction wrote.</summary>
public sealed record ExtractedFile(string Path, long Bytes);

/// <summary>
/// The outcome of an extraction. <paramref name="Skipped"/> counts the entries a <c>pattern</c> left
/// out, and <paramref name="Truncated"/> says the reported file list was cut short by its cap, not
/// that anything was left unextracted.
/// </summary>
public sealed record ZipExtractResult(
    string Path,
    string Destination,
    ExtractedFile[] Files,
    int Skipped,
    bool Overwrite,
    bool Truncated,
    string? Error);

/// <summary>
/// The outcome of packing. <paramref name="Bytes"/> is the uncompressed size of what went in and
/// <paramref name="CompressedBytes"/> the size of the archive that was produced.
/// </summary>
public sealed record ZipCreateResult(
    string Path,
    int Entries,
    long Bytes,
    long CompressedBytes,
    bool Overwritten,
    string? Error);
