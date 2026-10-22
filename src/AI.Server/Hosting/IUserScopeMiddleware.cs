namespace AI.Server.Hosting;

using Microsoft.AspNetCore.Http;

/// <summary>Runs the rest of a request in the scope of the user it comes from.</summary>
public interface IUserScopeMiddleware
{
    Task InvokeAsync(HttpContext context, RequestDelegate pipeline);
}
