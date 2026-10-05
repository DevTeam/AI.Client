namespace AI.Application.Resources;

using AI.Contracts.Resources;

public sealed class MediaFilePreviewFormat : IFilePreviewFormat
{
    public Task<FilePreview?> DescribeAsync(FilePreviewContext context, CancellationToken cancellationToken)
    {
        if (Directory.Exists(context.Path)) return Task.FromResult<FilePreview?>(null);
        var type = context.ContentType;
        var kind = type.StartsWith("image/", StringComparison.Ordinal) ? "image"
            : type.StartsWith("video/", StringComparison.Ordinal) ? "video"
            : type.StartsWith("audio/", StringComparison.Ordinal) ? "audio"
            : type == "application/pdf" ? "pdf" : null;
        return Task.FromResult(kind is null ? null : context.Describe(kind));
    }
}
