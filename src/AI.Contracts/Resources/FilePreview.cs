namespace AI.Contracts.Resources;

/// <param name="ChatFilesPath">
/// For a file in a chat's scratch directory, its path inside that directory (empty for the
/// directory itself), so the UI can show "Chat files › …" instead of the operating system path.
/// </param>
public sealed record FilePreview(string Path, string Name, string Kind, string ContentType, long Size,
    string? ContentUrl, IReadOnlyList<FilePreviewEntry> Entries, bool HasMoreEntries = false,
    string? ChatFilesPath = null);

public sealed record FilePreviewEntry(string Path, string Name, bool IsDirectory, long? Size = null,
    long? CompressedSize = null);

public sealed record FilePreviewText(string Text, int? NextOffset);
public sealed record ResourceAssetText(string Text, bool Truncated);
