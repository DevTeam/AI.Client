namespace AI.Server.Hosting.Endpoints;

using AI.Contracts;
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
                    HostProtocol.ApiVersion,
                    Status = "ready"
                }));
    }
}
