namespace AI.Server.Hosting.Endpoints;

using Microsoft.AspNetCore.Routing;

/// <summary>
/// One area of the HTTP API. Modules hold no services of their own: handlers take what they need
/// as parameters, so the container resolves them per request, after ASP.NET has built its
/// service provider. The routes and payloads are the Host-Web contract and must not drift.
/// </summary>
public interface IEndpointModule
{
    void Map(IEndpointRouteBuilder routes);
}
