namespace AI.Web.Resources;

using AI.Web.Components.FilePreviews;

public interface IFilePreviewViewers
{
    Type Resolve(string kind);
}

public interface IFilePreviewViewerRegistration
{
    IReadOnlyList<string> Kinds { get; }
    Type Component { get; }
}

public sealed class FilePreviewViewers(IFilePreviewViewerRegistration[] registrations) : IFilePreviewViewers
{
    private readonly Dictionary<string, Type> _components = registrations
        .SelectMany(registration => registration.Kinds.Select(kind => (kind, registration.Component)))
        .ToDictionary(item => item.kind, item => item.Component, StringComparer.Ordinal);

    public Type Resolve(string kind) => _components.GetValueOrDefault(kind, typeof(BinaryFilePreview));
}

public sealed class MediaFilePreviewRegistration : IFilePreviewViewerRegistration
{
    public IReadOnlyList<string> Kinds => ["image", "video", "audio", "pdf"];
    public Type Component => typeof(MediaFilePreview);
}

public sealed class TextFilePreviewRegistration : IFilePreviewViewerRegistration
{
    public IReadOnlyList<string> Kinds => ["text", "markdown", "diff"];
    public Type Component => typeof(TextFilePreview);
}

public sealed class DirectoryFilePreviewRegistration : IFilePreviewViewerRegistration
{
    public IReadOnlyList<string> Kinds => ["directory"];
    public Type Component => typeof(DirectoryFilePreview);
}

public sealed class ArchiveFilePreviewRegistration : IFilePreviewViewerRegistration
{
    public IReadOnlyList<string> Kinds => ["archive"];
    public Type Component => typeof(ArchiveFilePreview);
}
