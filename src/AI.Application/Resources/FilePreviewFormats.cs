namespace AI.Application.Resources;

using AI.Contracts.Resources;

public sealed class FilePreviewFormats(IFilePreviewFormat[] formats,
    ITextFilePreviewFormat fallback) : IFilePreviewFormats
{
    public async Task<FilePreview> DescribeAsync(FilePreviewContext context, CancellationToken cancellationToken)
    {
        foreach (var format in formats)
            if (await format.DescribeAsync(context, cancellationToken) is { } preview) return preview;
        return await fallback.DescribeAsync(context, cancellationToken);
    }
}

public sealed class TextFilePreviewFormat(IFilePreviewTextReader text) : ITextFilePreviewFormat
{
    public async Task<FilePreview> DescribeAsync(FilePreviewContext context, CancellationToken cancellationToken) =>
        context.Describe(await text.IsTextAsync(context.Path, cancellationToken) ? "text" : "binary");
}
