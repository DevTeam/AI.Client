namespace AI.Application.Resources;

using AI.Contracts.Resources;
using Microsoft.AspNetCore.StaticFiles;

public sealed class FilePreviewTextReader : IFilePreviewTextReader
{
    private readonly FileExtensionContentTypeProvider _types = new();

    public async Task<FilePreviewText> ReadAsync(string path, int offset, CancellationToken cancellationToken)
    {
        if (offset is < 0 or > 20_000_000) throw new ArgumentException("Text preview offset is out of range.");
        var canonical = path;
        if (!await IsTextAsync(canonical, cancellationToken)) throw new ArgumentException("This file is not supported as text.");
        using var reader = File.OpenText(canonical);
        var buffer = new char[32768];
        var skipped = 0;
        while (skipped < offset)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, offset - skipped)), cancellationToken);
            if (count == 0) return new FilePreviewText("", null);
            skipped += count;
        }
        var length = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
        var text = new string(buffer, 0, length);
        // JSON cannot carry half of a surrogate pair: keep an emoji on one side of a page.
        if (length > 0 && char.IsHighSurrogate(buffer[length - 1]) && reader.Peek() is >= 0xDC00 and <= 0xDFFF)
        {
            text += (char)reader.Read();
            length++;
        }
        return new FilePreviewText(text, reader.Peek() < 0 ? null : offset + length);
    }

    public async Task<bool> IsTextAsync(string path, CancellationToken cancellationToken)
    {
        if (_types.TryGetContentType(path, out var type) && (type.StartsWith("image/", StringComparison.Ordinal)
            || type.StartsWith("video/", StringComparison.Ordinal) || type.StartsWith("audio/", StringComparison.Ordinal)
            || type == "application/pdf")) return false;
        using var reader = File.OpenText(path);
        var buffer = new char[4096];
        var length = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
        for (var index = 0; index < length; index++)
            if (buffer[index] == '\uFFFD' || buffer[index] < ' ' && buffer[index] is not ('\t' or '\r' or '\n' or '\f')) return false;
        return true;
    }
}
