namespace AI.Server.Hosting.Endpoints;

using Application.Instructions;
using Application.Memory;
using Application.Projects;
using Contracts.Instructions;
using Contracts.Memory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>Long-term memory, project instructions and the standing prompt preview.</summary>
public sealed class MemoryEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/memory",
            (Guid? projectId, IMemoryService memory, CancellationToken token) => memory.ListAsync(projectId, token));

        routes.MapPost("/api/memory",
            async (CreateMemoryEntryRequest request, IMemoryService memory, CancellationToken token) =>
                ToResult(await memory.CreateAsync(request, MemoryAuthor.User, null, token)));

        routes.MapPut("/api/memory/{id:guid}",
            async (Guid id, Guid? projectId, UpdateMemoryEntryRequest request, IMemoryService memory, CancellationToken token) =>
                ToResult(await memory.UpdateAsync(id, projectId, request, MemoryAuthor.User, null, token)));

        routes.MapDelete("/api/memory/{id:guid}",
            async (Guid id, Guid? projectId, long revision, IMemoryService memory, CancellationToken token) =>
                ToResult(await memory.DeleteAsync(id, projectId, revision, token), noContent: true));

        routes.MapGet("/api/projects/{id:guid}/instructions",
            async (Guid id, IProjectInstructionsService instructions, CancellationToken token) =>
                await instructions.GetAsync(id, token) is { } document ? Results.Ok(document) : Results.NotFound());

        routes.MapPut("/api/projects/{id:guid}/instructions",
            async (Guid id, UpdateProjectInstructionsRequest request, IProjectInstructionsService instructions,
                CancellationToken token) =>
            {
                var result = await instructions.UpdateAsync(id, request, token);
                return result.Status switch
                {
                    ProjectInstructionsUpdateStatus.Updated => Results.Ok(result.Instructions),
                    ProjectInstructionsUpdateStatus.Conflict => Results.Conflict(result.Instructions),
                    ProjectInstructionsUpdateStatus.Rejected => Results.Problem(result.Error, statusCode: StatusCodes.Status400BadRequest),
                    _ => Results.NotFound()
                };
            });

        routes.MapGet("/api/projects/{id:guid}/model-context",
            async (Guid id, IProjectService projects, IStandingInstructions standing, CancellationToken token) =>
                await projects.GetAsync(id, token) is null
                    ? Results.NotFound()
                    : Results.Ok(await standing.BuildAsync(id, true, token)));
    }

    private static IResult ToResult(MemoryWriteResult result, bool noContent = false) => result.Status switch
    {
        MemoryWriteStatus.Saved => noContent ? Results.NoContent() : Results.Ok(result.Entry),
        MemoryWriteStatus.Conflict => Results.Conflict(result.Entry),
        MemoryWriteStatus.Rejected => Results.Problem(result.Error, statusCode: StatusCodes.Status400BadRequest),
        _ => Results.NotFound()
    };
}
