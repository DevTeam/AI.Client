namespace AI.Client.Server.Hosting;

using Microsoft.AspNetCore.Http;

/// <summary>Turns the exceptions services use to reject a request into HTTP problem responses.</summary>
public interface IApiExceptionHandler
{
    Task InvokeAsync(HttpContext context, RequestDelegate pipeline);
}
