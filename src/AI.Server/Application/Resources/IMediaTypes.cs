namespace AI.Application.Resources;

using System.Diagnostics.CodeAnalysis;

/// <summary>The media type a file's name implies, such as <c>image/png</c> for <c>a.png</c>.</summary>
public interface IMediaTypes
{
    bool TryGetContentType(string path, [NotNullWhen(true)] out string? contentType);
}
