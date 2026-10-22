namespace AI.Infrastructure.Workspace;

using System.Diagnostics.CodeAnalysis;
using AI.Application.Resources;
using Microsoft.AspNetCore.StaticFiles;

/// <summary>ASP.NET's table of file extensions, the one the Host serves static files with.</summary>
public sealed class FileExtensionMediaTypes : IMediaTypes
{
    private readonly FileExtensionContentTypeProvider _types = new();

    public bool TryGetContentType(string path, [NotNullWhen(true)] out string? contentType) =>
        _types.TryGetContentType(path, out contentType);
}
