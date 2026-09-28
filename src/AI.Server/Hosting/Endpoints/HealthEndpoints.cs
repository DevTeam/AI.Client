namespace AI.Server.Hosting.Endpoints;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

public sealed class HealthEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet(
            "/api/health",
            (IHostDescriptor metadata) => Results.Ok(
                new
                {
                    metadata.ProductName,
                    metadata.Version,
                    ApiVersion = 1,
                    Status = "ready"
                }));
    }
}
