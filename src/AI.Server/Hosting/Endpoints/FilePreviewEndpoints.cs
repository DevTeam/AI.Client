namespace AI.Server.Hosting.Endpoints;

using Application.Resources;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;

public sealed class FilePreviewEndpoints : IEndpointModule
{
    private readonly FileExtensionContentTypeProvider _types = new();

    public void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/projects/{projectId:guid}/resources/preview",
            (Guid projectId, string path, IFilePreviewService service, CancellationToken token) =>
                GuardAsync(async () => Results.Ok(await service.DescribeAsync(projectId, path, token))));
        routes.MapGet("/api/projects/{projectId:guid}/resources/preview-text",
            (Guid projectId, string path, int? offset, IFilePreviewService service, CancellationToken token) =>
                GuardAsync(async () => Results.Ok(await service.ReadTextAsync(projectId, path, offset ?? 0, token))));
        // Media elements cannot send the bridge's Authorization header. A short-lived, random
        // ticket grants this one file only; every range request checks project access again.
        routes.MapGet("/file-preview-content/{ticket}", async (string ticket, bool? download,
            IFilePreviewService service, HttpContext context, CancellationToken token) => await GuardAsync(async () =>
        {
            var path = await service.ResolveContentAsync(ticket, token);
            if (path is null) return Results.NotFound();
            _types.TryGetContentType(path, out var contentType);
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (contentType is "text/html" or "image/svg+xml")
                context.Response.Headers.ContentSecurityPolicy = "sandbox";
            // HTML/SVG cannot execute as a top-level document even if a preview URL is copied.
            return Results.File(path, contentType ?? "application/octet-stream",
                fileDownloadName: download == true ? Path.GetFileName(path) : null, enableRangeProcessing: true);
        }));
    }

    private static async Task<IResult> GuardAsync(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (UnauthorizedAccessException) { return Results.Problem("The project cannot read this path.", statusCode: 403); }
        catch (FileNotFoundException) { return Results.NotFound(); }
        catch (DirectoryNotFoundException) { return Results.NotFound(); }
        catch (IOException) { return Results.Problem("The file is unavailable or busy. Try again.", statusCode: 409); }
    }
}
