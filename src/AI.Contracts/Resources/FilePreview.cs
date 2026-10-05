namespace AI.Contracts.Resources;

public sealed record FilePreview(string Path, string Name, string Kind, string ContentType, long Size,
    string? ContentUrl, IReadOnlyList<FilePreviewEntry> Entries, bool HasMoreEntries = false);

public sealed record FilePreviewEntry(string Path, string Name, bool IsDirectory, long? Size = null,
    long? CompressedSize = null);

public sealed record FilePreviewText(string Text, int? NextOffset);
