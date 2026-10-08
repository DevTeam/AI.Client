namespace AI.Application.Resources;

using AI.Contracts.Resources;

public sealed class MarkupFilePreviewFormat(IFilePreviewTextReader text) : IFilePreviewFormat
{
    public async Task<FilePreview?> DescribeAsync(FilePreviewContext context, CancellationToken cancellationToken)
    {
        if (context.IsDirectory) return null;
        var kind = Path.GetExtension(context.Path).ToLowerInvariant() switch
        {
            ".md" or ".markdown" or ".mdown" or ".mkd" => "markdown",
            ".diff" or ".patch" => "diff",
            _ => null
        };
        return kind is not null && await text.IsTextAsync(context.Path, cancellationToken)
            ? context.Describe(kind) : null;
    }
}
