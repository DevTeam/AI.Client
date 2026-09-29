namespace AI.Server.Hosting.Endpoints;

using AI.Application.Skills;
using AI.Contracts.Skills;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

public sealed class SkillEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/skills", async (Guid? projectId, ISkillCatalog catalog, CancellationToken token) =>
            Results.Ok(await catalog.ListAsync(projectId, token)));
        routes.MapGet("/api/skills/runs", (ISkillRunner runner) => Results.Ok(runner.ListRecent()));
        routes.MapPost("/api/skills", async (SkillWriteRequest request, ISkillCatalog catalog, CancellationToken token) =>
            ToResult(await catalog.SaveAsync(request, token)));
        routes.MapPut("/api/skills/{id}", async (string id, SkillWriteRequest request, ISkillCatalog catalog,
            CancellationToken token) =>
        {
            try
            {
                if (SkillMarkdown.Parse(request.Content, request.Scope, request.ProjectId).Id != id)
                    return Results.BadRequest("The skill ID does not match the URL.");
            }
            catch (Exception error) when (error is ArgumentException or FormatException or JsonException)
            {
                return Results.BadRequest(error.Message);
            }
            return ToResult(await catalog.SaveAsync(request, token));
        });
        routes.MapDelete("/api/skills/{id}", async (string id, string scope, Guid? projectId, long revision,
            ISkillCatalog catalog, CancellationToken token) =>
            ToResult(await catalog.DeleteAsync(id, scope, projectId, revision, token)));
    }

    private static IResult ToResult(SkillWriteResult result) => result.Status switch
    {
        "Saved" => Results.Ok(result.Skill),
        "Deleted" => Results.NoContent(),
        "Conflict" => Results.Conflict(result.Skill),
        "NotFound" => Results.NotFound(),
        _ => Results.Problem(result.Error, statusCode: StatusCodes.Status400BadRequest)
    };
}
