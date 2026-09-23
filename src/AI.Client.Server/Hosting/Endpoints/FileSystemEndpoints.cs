namespace AI.Client.Server.Hosting.Endpoints;

using Application.Projects;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>
/// A directory grant names a path on this machine, and the browser the UI runs in cannot name one:
/// a folder chosen through <c>webkitdirectory</c> or <c>showDirectoryPicker()</c> arrives without
/// its absolute path, which is the one thing a grant is made of. So the host does the browsing and
/// the UI walks it. That makes these endpoints a read-only window onto the local file system for
/// anyone who can reach the API — today only the origins in <c>Cors:AllowedOrigins</c>, all of
/// them loopback. <see cref="ServerOptions.BrowseEnabled"/> lets a deployment that stops being
/// local close the window without a code change.
/// </summary>
public sealed class FileSystemEndpoints(ServerOptions options) : IEndpointModule
{
    public void Map(IEndpointRouteBuilder routes)
    {
        if (!options.BrowseEnabled)
        {
            return;
        }

        routes.MapGet(
            "/api/filesystem/roots",
            (IDirectoryBrowser browser, CancellationToken cancellationToken) => browser.ListRootsAsync(cancellationToken));

        routes.MapGet(
            "/api/filesystem/directories",
            async (string path, bool? includeFiles, IDirectoryBrowser browser, CancellationToken cancellationToken) =>
                await browser.ListAsync(path, includeFiles ?? false, cancellationToken) is { } listing
                    ? Results.Ok(listing)
                    : Results.NotFound());

        routes.MapGet(
            "/api/filesystem/resolve",
            (string path, IDirectoryBrowser browser, CancellationToken cancellationToken) =>
                browser.ResolveAsync(path, cancellationToken));
    }
}
