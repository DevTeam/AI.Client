namespace AI.Server.Hosting.Endpoints;

using AI.Application.Skills;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

public sealed class SkillEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/skills", (ISkillCatalog catalog) => Results.Ok(catalog.List()));
    }
}
