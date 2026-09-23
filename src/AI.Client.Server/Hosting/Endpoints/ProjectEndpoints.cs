namespace AI.Client.Server.Hosting.Endpoints;

using Application.Projects;
using Application.Runs;
using Contracts.Projects;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

public sealed class ProjectEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet(
            "/api/projects",
            (IProjectService service, CancellationToken cancellationToken) =>
                service.ListAsync(cancellationToken));

        routes.MapGet(
            "/api/projects/{id:guid}",
            async (Guid id, IProjectService service, CancellationToken cancellationToken) =>
            {
                var project = await service.GetAsync(id, cancellationToken);
                return project is null ? Results.NotFound() : Results.Ok(project);
            });

        routes.MapPost(
            "/api/projects",
            async (CreateProjectRequest request, IProjectService service, CancellationToken cancellationToken) =>
            {
                var project = await service.CreateAsync(request, cancellationToken);
                return Results.Created($"/api/projects/{project.Id}", project);
            });

        routes.MapPut(
            "/api/projects/{id:guid}",
            async (Guid id, UpdateProjectRequest request, IProjectService service, CancellationToken cancellationToken) =>
            {
                var result = await service.UpdateAsync(id, request, cancellationToken);
                return result.Status switch
                {
                    ProjectUpdateStatus.Updated => Results.Ok(result.Project),
                    ProjectUpdateStatus.Conflict => Results.Conflict(result),
                    _ => Results.NotFound()
                };
            });

        routes.MapPut(
            "/api/projects/{id:guid}/security",
            async (Guid id, UpdateProjectSecurityRequest request, IProjectService service, IDirectoryBrowser browser, CancellationToken cancellationToken) =>
            {
                // Grants are canonicalised on the way in, not on the way out of the picker, because a path
                // can also be typed or pasted. Without this, "C:\Proj\x", "c:\proj\x" and "C:\Proj\..\Proj\x"
                // become three grants over one directory, and revoking the one you can see leaves two.
                request = request with
                {
                    DirectoryGrants = request.DirectoryGrants
                        .Select(item => item with { CanonicalRoot = browser.Canonicalize(item.CanonicalRoot) })
                        .ToArray()
                };
                var result = await service.UpdateSecurityAsync(id, request, cancellationToken);
                return result.Status switch
                {
                    ProjectUpdateStatus.Updated => Results.Ok(result.Project),
                    ProjectUpdateStatus.Conflict => Results.Conflict(result),
                    _ => Results.NotFound()
                };
            });

        routes.MapPut("/api/projects/{id:guid}/tool-policies",
            async (Guid id, ToolPolicySettings policy, IProjectService service, CancellationToken token) =>
                await service.SetToolPolicyAsync(id, policy, token) is { } project ? Results.Ok(project) : Results.NotFound());

        routes.MapDelete("/api/projects/{id:guid}/tool-policies/{serverId:guid}",
            async (Guid id, Guid serverId, string name, string schemaHash, IProjectService service, CancellationToken token) =>
                await service.RemoveToolPolicyAsync(id, serverId, name, schemaHash, token) is { } project ? Results.Ok(project) : Results.NotFound());

        routes.MapDelete(
            "/api/projects/{id:guid}",
            async (Guid id, long revision, IProjectService _, IChatRunDispatcher runs, CancellationToken cancellationToken) =>
            {
                var result = await runs.DeleteProjectAsync(id, revision, cancellationToken);
                return result.IsDeleted
                    ? Results.NoContent()
                    : result.Revision == 0 ? Results.NotFound() : Results.Conflict(result);
            });
    }
}
